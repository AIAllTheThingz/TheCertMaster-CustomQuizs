using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizAPI.Services;
using System.DirectoryServices.Protocols;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin/active-directory")]
    [Authorize(Roles = "Admin")]
    public class ActiveDirectoryAdminController : ControllerBase
    {
        private readonly IActiveDirectorySettingsStore _store;

        public ActiveDirectoryAdminController(IActiveDirectorySettingsStore store)
        {
            _store = store;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var settings = await _store.GetAsync();
            return Ok(new
            {
                enabled = settings.Enabled,
                host = settings.Host,
                port = settings.Port,
                sslPort = settings.SslPort,
                adminGroups = settings.AdminGroups,
                userGroups = settings.UserGroups
            });
        }

        public sealed class UpdateActiveDirectoryRequest
        {
            public bool Enabled { get; set; }
            public string Host { get; set; } = string.Empty;
            public int Port { get; set; } = 389;
            public int SslPort { get; set; } = 636;
            public string AdminGroups { get; set; } = string.Empty;
            public string UserGroups { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] UpdateActiveDirectoryRequest req)
        {
            if (req == null) return BadRequest("Request body is required.");
            if (req.Enabled && string.IsNullOrWhiteSpace(req.Host)) return BadRequest("LDAP host is required when LDAP is enabled.");
            if (req.Port <= 0 && req.SslPort <= 0) return BadRequest("At least one LDAP port must be configured.");

            var adminGroups = SplitGroups(req.AdminGroups);
            var userGroups = SplitGroups(req.UserGroups);

            var options = await _store.GetAsync();
            options.Enabled = req.Enabled;
            options.Host = req.Host.Trim();
            options.Port = req.Port > 0 ? req.Port : 0;
            options.SslPort = req.SslPort > 0 ? req.SslPort : 0;
            options.AdminGroups = adminGroups;
            options.UserGroups = userGroups;
            options.RequireMappedRole = true;
            options.DefaultRole = "User";

            await _store.SaveAsync(options);
            return Ok(new { message = "LDAP settings saved." });
        }

        [HttpPost("test")]
        public async Task<IActionResult> TestConnection()
        {
            var settings = await _store.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                return BadRequest("LDAP host is not configured.");
            }

            var results = new List<object>();
            if (settings.Port > 0)
            {
                results.Add(TestEndpoint(settings.Host, settings.Port, useSsl: false));
            }

            if (settings.SslPort > 0)
            {
                results.Add(TestEndpoint(settings.Host, settings.SslPort, useSsl: true));
            }

            return Ok(new
            {
                host = settings.Host,
                results
            });
        }

        private static List<string> SplitGroups(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static object TestEndpoint(string host, int port, bool useSsl)
        {
            try
            {
                var identifier = new LdapDirectoryIdentifier(host, port);
                using var connection = new LdapConnection(identifier)
                {
                    AuthType = AuthType.Anonymous,
                    Timeout = TimeSpan.FromSeconds(8)
                };

                connection.SessionOptions.ProtocolVersion = 3;
                connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
                connection.SessionOptions.SecureSocketLayer = useSsl;
                connection.Bind();

                var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
                var response = (SearchResponse)connection.SendRequest(request);
                var namingContext = response.Entries.Count > 0
                    ? response.Entries[0].Attributes["defaultNamingContext"]?[0]?.ToString()
                    : string.Empty;

                return new
                {
                    port,
                    useSsl,
                    reachable = true,
                    message = string.IsNullOrWhiteSpace(namingContext)
                        ? "Connected. LDAP root query succeeded."
                        : $"Connected. defaultNamingContext={namingContext}"
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    port,
                    useSsl,
                    reachable = false,
                    message = ex.Message
                };
            }
        }
    }
}
