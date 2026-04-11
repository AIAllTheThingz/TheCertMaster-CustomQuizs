using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin/system-status")]
    [Authorize(Roles = "Admin")]
    public sealed class SystemStatusAdminController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public SystemStatusAdminController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            var version = entryAssembly?.GetName().Version?.ToString() ?? "unknown";
            var informationalVersion = entryAssembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? version;

            var connectionString = _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
            var databaseName = TryExtractDatabaseName(connectionString);
            var requestHost = HttpContext.Request.Host.HasValue ? HttpContext.Request.Host.Value : "unknown";
            var requestScheme = HttpContext.Request.Scheme;
            var baseUrl = $"{requestScheme}://{requestHost}";

            return Ok(new
            {
                machineName = Environment.MachineName,
                environmentName = _environment.EnvironmentName,
                applicationName = _environment.ApplicationName,
                version,
                informationalVersion,
                contentRootPath = _environment.ContentRootPath,
                webRootPath = _environment.WebRootPath ?? string.Empty,
                databaseName,
                swaggerEnabled = _configuration.GetValue<bool?>("Swagger:Enabled") ?? _environment.IsDevelopment(),
                ldapEnabled = _configuration.GetValue<bool>("ActiveDirectory:Enabled"),
                ldapHost = _configuration["ActiveDirectory:Host"] ?? string.Empty,
                healthUrl = $"{baseUrl}/health",
                readyHealthUrl = $"{baseUrl}/health/ready",
                swaggerUrl = $"{baseUrl}/swagger"
            });
        }

        private static string TryExtractDatabaseName(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return string.Empty;
            }

            var segments = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var kvp = segment.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kvp.Length != 2)
                {
                    continue;
                }

                if (kvp[0].Equals("Database", StringComparison.OrdinalIgnoreCase)
                    || kvp[0].Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                {
                    return kvp[1];
                }
            }

            return string.Empty;
        }
    }
}
