using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;

namespace QuizAPI.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class ActiveDirectoryAuthService : IActiveDirectoryAuthService
    {
        private readonly ActiveDirectoryOptions _options;
        private readonly ILogger<ActiveDirectoryAuthService> _logger;

        public ActiveDirectoryAuthService(
            Microsoft.Extensions.Options.IOptions<ActiveDirectoryOptions> options,
            ILogger<ActiveDirectoryAuthService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Task<ActiveDirectoryAuthResult?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows() || !_options.Enabled || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                return Task.FromResult<ActiveDirectoryAuthResult?>(null);
            }

            return Task.Run(() => AuthenticateInternal(login.Trim(), password), cancellationToken);
        }

        private ActiveDirectoryAuthResult? AuthenticateInternal(string login, string password)
        {
            try
            {
                using var context = CreatePrincipalContext();
                var credential = BuildCredentialCandidates(login)
                    .FirstOrDefault(candidate => context.ValidateCredentials(candidate, password, ContextOptions.Negotiate));

                if (string.IsNullOrWhiteSpace(credential))
                {
                    return null;
                }

                using var principal = FindUserPrincipal(context, login, credential);
                if (principal is null)
                {
                    _logger.LogWarning("Active Directory credentials validated for {Login}, but no principal could be loaded.", login);
                    return null;
                }

                var email = (principal.EmailAddress ?? login).Trim();
                var groups = GetGroupNames(principal);
                var roles = MapRoles(groups);

                if (_options.RequireMappedRole && roles.Count == 0)
                {
                    _logger.LogWarning("Active Directory user {Login} authenticated but no mapped application role was found.", login);
                    return null;
                }

                return new ActiveDirectoryAuthResult
                {
                    Email = email,
                    UserName = string.IsNullOrWhiteSpace(principal.UserPrincipalName) ? email : principal.UserPrincipalName,
                    FirstName = principal.GivenName ?? string.Empty,
                    LastName = principal.Surname ?? string.Empty,
                    Groups = groups,
                    Roles = roles
                };
            }
            catch (PrincipalServerDownException ex)
            {
                _logger.LogError(ex, "Active Directory server is unavailable.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Active Directory authentication failed unexpectedly for {Login}.", login);
                return null;
            }
        }

        private PrincipalContext CreatePrincipalContext()
        {
            var domain = string.IsNullOrWhiteSpace(_options.Domain) ? null : _options.Domain.Trim();
            var container = string.IsNullOrWhiteSpace(_options.Container) ? null : _options.Container.Trim();

            if (string.IsNullOrWhiteSpace(domain) && string.IsNullOrWhiteSpace(container))
            {
                return new PrincipalContext(ContextType.Domain);
            }

            if (string.IsNullOrWhiteSpace(container))
            {
                return new PrincipalContext(ContextType.Domain, domain);
            }

            return new PrincipalContext(ContextType.Domain, domain, container);
        }

        private IEnumerable<string> BuildCredentialCandidates(string login)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    candidates.Add(value.Trim());
                }
            }

            AddCandidate(login);

            var localPart = login.Contains('@', StringComparison.Ordinal)
                ? login[..login.IndexOf('@')]
                : login;

            AddCandidate(localPart);

            if (!string.IsNullOrWhiteSpace(_options.NetBiosDomain))
            {
                AddCandidate($"{_options.NetBiosDomain.Trim()}\\{localPart}");
            }

            var upnSuffix = !string.IsNullOrWhiteSpace(_options.UserPrincipalSuffix)
                ? _options.UserPrincipalSuffix.Trim()
                : (!string.IsNullOrWhiteSpace(_options.Domain) && _options.Domain.Contains('.', StringComparison.Ordinal)
                    ? _options.Domain.Trim()
                    : string.Empty);

            if (!string.IsNullOrWhiteSpace(upnSuffix))
            {
                AddCandidate($"{localPart}@{upnSuffix}");
            }

            return candidates;
        }

        private static UserPrincipal? FindUserPrincipal(PrincipalContext context, string login, string validatedCredential)
        {
            var localPart = login.Contains('@', StringComparison.Ordinal)
                ? login[..login.IndexOf('@')]
                : login;

            return UserPrincipal.FindByIdentity(context, IdentityType.UserPrincipalName, validatedCredential)
                ?? UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, localPart);
        }

        private static List<string> GetGroupNames(UserPrincipal principal)
        {
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var authGroup in principal.GetAuthorizationGroups())
                {
                    if (!string.IsNullOrWhiteSpace(authGroup.SamAccountName))
                    {
                        groups.Add(authGroup.SamAccountName);
                    }

                    if (!string.IsNullOrWhiteSpace(authGroup.Name))
                    {
                        groups.Add(authGroup.Name);
                    }
                }
            }
            catch
            {
                // Some environments restrict reading authorization groups; fall back to direct groups only.
                if (principal.GetGroups() is { } directGroups)
                {
                    foreach (var group in directGroups)
                    {
                        if (!string.IsNullOrWhiteSpace(group.SamAccountName))
                        {
                            groups.Add(group.SamAccountName);
                        }

                        if (!string.IsNullOrWhiteSpace(group.Name))
                        {
                            groups.Add(group.Name);
                        }
                    }
                }
            }

            return groups.OrderBy(x => x).ToList();
        }

        private List<string> MapRoles(IReadOnlyCollection<string> groups)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (MatchesAny(groups, _options.AdminGroups))
            {
                roles.Add("Admin");
            }

            if (MatchesAny(groups, _options.UserGroups))
            {
                roles.Add("User");
            }

            if (roles.Count == 0 && !_options.RequireMappedRole && !string.IsNullOrWhiteSpace(_options.DefaultRole))
            {
                roles.Add(_options.DefaultRole.Trim());
            }

            return roles.OrderBy(x => x).ToList();
        }

        private static bool MatchesAny(IReadOnlyCollection<string> groups, IReadOnlyCollection<string> configuredGroups)
        {
            if (groups.Count == 0 || configuredGroups.Count == 0)
            {
                return false;
            }

            return configuredGroups.Any(configured =>
                groups.Contains(configured, StringComparer.OrdinalIgnoreCase));
        }
    }
}
