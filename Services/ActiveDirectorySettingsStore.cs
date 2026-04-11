using System.Text.Json;

namespace QuizAPI.Services
{
    public interface IActiveDirectorySettingsStore
    {
        Task<ActiveDirectoryOptions> GetAsync();
        Task SaveAsync(ActiveDirectoryOptions options);
    }

    public sealed class FileActiveDirectorySettingsStore : IActiveDirectorySettingsStore
    {
        private readonly string _filePath;
        private readonly ActiveDirectoryOptions _defaults;
        private readonly object _lock = new();

        public FileActiveDirectorySettingsStore(IConfiguration config, IWebHostEnvironment env)
        {
            _defaults = new ActiveDirectoryOptions();
            var section = config.GetSection("ActiveDirectory");
            if (section.Exists())
            {
                section.Bind(_defaults);
            }

            var appData = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "active_directory_settings.json");
        }

        public Task<ActiveDirectoryOptions> GetAsync()
        {
            lock (_lock)
            {
                var current = Clone(_defaults);

                if (File.Exists(_filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_filePath);
                        var saved = JsonSerializer.Deserialize<ActiveDirectoryOptions>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (saved != null)
                        {
                            Apply(current, saved);
                        }
                    }
                    catch
                    {
                    }
                }

                return Task.FromResult(current);
            }
        }

        public async Task SaveAsync(ActiveDirectoryOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            lock (_lock)
            {
                var json = JsonSerializer.Serialize(Clone(options), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_filePath, json);
            }

            await Task.CompletedTask;
        }

        private static ActiveDirectoryOptions Clone(ActiveDirectoryOptions src)
        {
            return new ActiveDirectoryOptions
            {
                Enabled = src.Enabled,
                Host = src.Host,
                Port = src.Port,
                SslPort = src.SslPort,
                Domain = src.Domain,
                Container = src.Container,
                NetBiosDomain = src.NetBiosDomain,
                UserPrincipalSuffix = src.UserPrincipalSuffix,
                RequireMappedRole = src.RequireMappedRole,
                DefaultRole = src.DefaultRole,
                AdminGroups = src.AdminGroups?.ToList() ?? new List<string>(),
                UserGroups = src.UserGroups?.ToList() ?? new List<string>()
            };
        }

        private static void Apply(ActiveDirectoryOptions target, ActiveDirectoryOptions src)
        {
            target.Enabled = src.Enabled;
            if (!string.IsNullOrWhiteSpace(src.Host)) target.Host = src.Host.Trim();
            if (src.Port > 0) target.Port = src.Port;
            if (src.SslPort > 0) target.SslPort = src.SslPort;
            if (!string.IsNullOrWhiteSpace(src.Domain)) target.Domain = src.Domain.Trim();
            if (!string.IsNullOrWhiteSpace(src.Container)) target.Container = src.Container.Trim();
            if (!string.IsNullOrWhiteSpace(src.NetBiosDomain)) target.NetBiosDomain = src.NetBiosDomain.Trim();
            if (!string.IsNullOrWhiteSpace(src.UserPrincipalSuffix)) target.UserPrincipalSuffix = src.UserPrincipalSuffix.Trim();
            target.RequireMappedRole = src.RequireMappedRole;
            if (!string.IsNullOrWhiteSpace(src.DefaultRole)) target.DefaultRole = src.DefaultRole.Trim();
            target.AdminGroups = src.AdminGroups?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            target.UserGroups = src.UserGroups?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        }
    }
}
