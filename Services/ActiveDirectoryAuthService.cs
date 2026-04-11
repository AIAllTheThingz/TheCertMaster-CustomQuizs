using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.Versioning;

namespace QuizAPI.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class ActiveDirectoryAuthService : IActiveDirectoryAuthService
    {
        private readonly IActiveDirectorySettingsStore _settingsStore;
        private readonly ILogger<ActiveDirectoryAuthService> _logger;

        public ActiveDirectoryAuthService(
            IActiveDirectorySettingsStore settingsStore,
            ILogger<ActiveDirectoryAuthService> logger)
        {
            _settingsStore = settingsStore;
            _logger = logger;
        }

        public async Task<ActiveDirectoryAuthResult?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var options = await _settingsStore.GetAsync();
            if (!options.Enabled)
            {
                return null;
            }

            return await Task.Run(() => AuthenticateInternal(options, login.Trim(), password), cancellationToken);
        }

        private ActiveDirectoryAuthResult? AuthenticateInternal(ActiveDirectoryOptions options, string login, string password)
        {
            try
            {
                foreach (var endpoint in BuildEndpoints(options))
                {
                    foreach (var credential in BuildCredentialCandidates(options, login))
                    {
                        var result = TryAuthenticateAgainstEndpoint(options, endpoint, login, credential, password);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }

                return null;
            }
            catch (LdapException ex)
            {
                _logger.LogError(ex, "LDAP server is unavailable.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LDAP authentication failed unexpectedly for {Login}.", login);
                return null;
            }
        }

        private ActiveDirectoryAuthResult? TryAuthenticateAgainstEndpoint(ActiveDirectoryOptions options, LdapEndpoint endpoint, string login, string credential, string password)
        {
            using var connection = CreateConnection(endpoint);
            try
            {
                connection.Bind(new NetworkCredential(credential, password));
            }
            catch (LdapException)
            {
                return null;
            }

            var searchBase = string.IsNullOrWhiteSpace(options.Container)
                ? GetDefaultNamingContext(connection)
                : options.Container.Trim();

            var entry = FindUserEntry(connection, searchBase, login, credential);
            if (entry is null)
            {
                _logger.LogWarning("LDAP credentials validated for {Login}, but no principal could be loaded.", login);
                return null;
            }

            var email = GetAttribute(entry, "mail");
            var userPrincipalName = GetAttribute(entry, "userPrincipalName");
            var firstName = GetAttribute(entry, "givenName");
            var lastName = GetAttribute(entry, "sn");
            var groups = GetGroupNames(entry);
            var roles = MapRoles(options, groups);

            if (options.RequireMappedRole && roles.Count == 0)
            {
                _logger.LogWarning("LDAP user {Login} authenticated but no mapped application role was found.", login);
                return null;
            }

            return new ActiveDirectoryAuthResult
            {
                Email = string.IsNullOrWhiteSpace(email) ? login : email,
                UserName = string.IsNullOrWhiteSpace(userPrincipalName) ? login : userPrincipalName,
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                Groups = groups,
                Roles = roles
            };
        }

        private static LdapConnection CreateConnection(LdapEndpoint endpoint)
        {
            var identifier = new LdapDirectoryIdentifier(endpoint.Host, endpoint.Port);
            var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate,
                Timeout = TimeSpan.FromSeconds(10)
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.SecureSocketLayer = endpoint.UseSsl;
            return connection;
        }

        private static IEnumerable<LdapEndpoint> BuildEndpoints(ActiveDirectoryOptions options)
        {
            var endpoints = new List<LdapEndpoint>();
            var host = string.IsNullOrWhiteSpace(options.Host) ? options.Domain.Trim() : options.Host.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                yield break;
            }

            if (options.SslPort > 0)
            {
                endpoints.Add(new LdapEndpoint(host, options.SslPort, true));
            }

            if (options.Port > 0)
            {
                endpoints.Add(new LdapEndpoint(host, options.Port, false));
            }

            foreach (var endpoint in endpoints
                .GroupBy(x => $"{x.Host}:{x.Port}:{x.UseSsl}")
                .Select(x => x.First()))
            {
                yield return endpoint;
            }
        }

        private static IEnumerable<string> BuildCredentialCandidates(ActiveDirectoryOptions options, string login)
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

            if (!string.IsNullOrWhiteSpace(options.NetBiosDomain))
            {
                AddCandidate($"{options.NetBiosDomain.Trim()}\\{localPart}");
            }

            var upnSuffix = !string.IsNullOrWhiteSpace(options.UserPrincipalSuffix)
                ? options.UserPrincipalSuffix.Trim()
                : (!string.IsNullOrWhiteSpace(options.Domain) && options.Domain.Contains('.', StringComparison.Ordinal)
                    ? options.Domain.Trim()
                    : string.Empty);

            if (!string.IsNullOrWhiteSpace(upnSuffix))
            {
                AddCandidate($"{localPart}@{upnSuffix}");
            }

            return candidates;
        }

        private static string GetDefaultNamingContext(LdapConnection connection)
        {
            var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
            var response = (SearchResponse)connection.SendRequest(request);
            var entry = response.Entries.Count > 0 ? response.Entries[0] : null;
            var namingContext = entry?.Attributes["defaultNamingContext"]?[0]?.ToString();
            if (string.IsNullOrWhiteSpace(namingContext))
            {
                throw new InvalidOperationException("LDAP defaultNamingContext could not be resolved.");
            }

            return namingContext;
        }

        private static SearchResultEntry? FindUserEntry(LdapConnection connection, string searchBase, string login, string credential)
        {
            var localPart = login.Contains('@', StringComparison.Ordinal)
                ? login[..login.IndexOf('@')]
                : login;

            var lookupValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EscapeLdapFilterValue(login),
                EscapeLdapFilterValue(localPart),
                EscapeLdapFilterValue(credential)
            };

            var filter = "(|" + string.Join(string.Empty, lookupValues.Select(value =>
                $"(userPrincipalName={value})(mail={value})(sAMAccountName={value})")) + ")";

            var request = new SearchRequest(
                searchBase,
                filter,
                SearchScope.Subtree,
                "mail",
                "userPrincipalName",
                "givenName",
                "sn",
                "memberOf");

            var response = (SearchResponse)connection.SendRequest(request);
            return response.Entries.Count > 0 ? response.Entries[0] : null;
        }

        private static string? GetAttribute(SearchResultEntry entry, string name)
        {
            return entry.Attributes.Contains(name) && entry.Attributes[name].Count > 0
                ? entry.Attributes[name][0]?.ToString()
                : null;
        }

        private static List<string> GetGroupNames(SearchResultEntry entry)
        {
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!entry.Attributes.Contains("memberOf"))
            {
                return groups.ToList();
            }

            foreach (var raw in entry.Attributes["memberOf"].GetValues(typeof(string)))
            {
                var dn = raw?.ToString();
                if (string.IsNullOrWhiteSpace(dn))
                {
                    continue;
                }

                var cn = ExtractCommonName(dn);
                if (!string.IsNullOrWhiteSpace(cn))
                {
                    groups.Add(cn);
                }
            }

            return groups.OrderBy(x => x).ToList();
        }

        private static string? ExtractCommonName(string distinguishedName)
        {
            var parts = distinguishedName.Split(',');
            var cnPart = parts.FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
            return cnPart?.Substring(3);
        }

        private static string EscapeLdapFilterValue(string value)
        {
            return value
                .Replace("\\", "\\5c", StringComparison.Ordinal)
                .Replace("*", "\\2a", StringComparison.Ordinal)
                .Replace("(", "\\28", StringComparison.Ordinal)
                .Replace(")", "\\29", StringComparison.Ordinal)
                .Replace("\0", "\\00", StringComparison.Ordinal);
        }

        private List<string> MapRoles(ActiveDirectoryOptions options, IReadOnlyCollection<string> groups)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (MatchesAny(groups, options.AdminGroups))
            {
                roles.Add("Admin");
            }

            if (MatchesAny(groups, options.UserGroups))
            {
                roles.Add("User");
            }

            if (roles.Count == 0 && !options.RequireMappedRole && !string.IsNullOrWhiteSpace(options.DefaultRole))
            {
                roles.Add(options.DefaultRole.Trim());
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

        private sealed record LdapEndpoint(string Host, int Port, bool UseSsl);
    }
}
