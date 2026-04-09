using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace QuizAPI.Services
{
    public interface ISmtpSettingsStore
    {
        Task<SmtpOptions> GetAsync();
        Task SaveAsync(SmtpOptions options, bool keepExistingPasswordWhenBlank);
    }

    public sealed class FileSmtpSettingsStore : ISmtpSettingsStore
    {
        private readonly string _filePath;
        private readonly SmtpOptions _defaults;
        private readonly IDataProtector _protector;
        private readonly object _lock = new();

        private sealed class PersistedSmtpOptions
        {
            public string Host { get; set; } = "";
            public int Port { get; set; } = 25;
            public bool UseStartTls { get; set; }
            public bool UseSsl { get; set; }
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string ProtectedPassword { get; set; } = "";
            public string FromEmail { get; set; } = "";
            public string FromName { get; set; } = "QuizAPI";
            public string NotificationEmail { get; set; } = "";
        }

        public FileSmtpSettingsStore(IConfiguration config, IWebHostEnvironment env, IDataProtectionProvider dataProtectionProvider)
        {
            _defaults = new SmtpOptions();
            var section = config.GetSection("Smtp");
            if (section.Exists())
            {
                section.Bind(_defaults);
            }

            _protector = dataProtectionProvider.CreateProtector("QuizAPI.SmtpSettings.Password.v1");

            var appData = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "smtp_settings.json");
        }

        public Task<SmtpOptions> GetAsync()
        {
            lock (_lock)
            {
                var current = Clone(_defaults);

                if (File.Exists(_filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_filePath);
                        var saved = JsonSerializer.Deserialize<PersistedSmtpOptions>(json, new JsonSerializerOptions
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

        public async Task SaveAsync(SmtpOptions options, bool keepExistingPasswordWhenBlank)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            lock (_lock)
            {
                var persisted = ToPersisted(Clone(options));

                if (keepExistingPasswordWhenBlank && string.IsNullOrWhiteSpace(options.Password))
                {
                    try
                    {
                        if (File.Exists(_filePath))
                        {
                            var jsonOld = File.ReadAllText(_filePath);
                            var old = JsonSerializer.Deserialize<PersistedSmtpOptions>(jsonOld, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (old != null)
                            {
                                persisted.Password = old.Password ?? "";
                                persisted.ProtectedPassword = old.ProtectedPassword ?? "";
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }

            await Task.CompletedTask;
        }

        private static SmtpOptions Clone(SmtpOptions src)
        {
            return new SmtpOptions
            {
                Host = src.Host,
                Port = src.Port,
                UseStartTls = src.UseStartTls,
                UseSsl = src.UseSsl,
                Username = src.Username,
                Password = src.Password,
                FromEmail = src.FromEmail,
                FromName = src.FromName,
                NotificationEmail = src.NotificationEmail
            };
        }

        private void Apply(SmtpOptions target, PersistedSmtpOptions src)
        {
            if (!string.IsNullOrWhiteSpace(src.Host)) target.Host = src.Host;
            if (src.Port != 0) target.Port = src.Port;
            target.UseStartTls = src.UseStartTls;
            target.UseSsl = src.UseSsl;
            target.Username = src.Username ?? "";
            target.Password = UnprotectPassword(src);
            if (!string.IsNullOrWhiteSpace(src.FromEmail)) target.FromEmail = src.FromEmail;
            if (!string.IsNullOrWhiteSpace(src.FromName)) target.FromName = src.FromName;
            if (!string.IsNullOrWhiteSpace(src.NotificationEmail)) target.NotificationEmail = src.NotificationEmail;
        }

        private PersistedSmtpOptions ToPersisted(SmtpOptions src)
        {
            return new PersistedSmtpOptions
            {
                Host = src.Host,
                Port = src.Port,
                UseStartTls = src.UseStartTls,
                UseSsl = src.UseSsl,
                Username = src.Username,
                Password = "",
                ProtectedPassword = ProtectPassword(src.Password ?? ""),
                FromEmail = src.FromEmail,
                FromName = src.FromName,
                NotificationEmail = src.NotificationEmail
            };
        }

        private string UnprotectPassword(PersistedSmtpOptions src)
        {
            if (!string.IsNullOrWhiteSpace(src.ProtectedPassword))
            {
                try
                {
                    return _protector.Unprotect(src.ProtectedPassword);
                }
                catch
                {
                    return "";
                }
            }

            // Legacy plaintext fallback so existing settings keep working until next save.
            return src.Password ?? "";
        }

        private string ProtectPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return "";

            return _protector.Protect(password);
        }
    }
}
