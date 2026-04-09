using System.Net;
using System.Net.Mail;

namespace QuizAPI.Services
{
    public interface IEmailService
    {
        Task SendAsync(string toEmail, string subject, string bodyText);
    }

    public sealed class EmailService : IEmailService
    {
        private readonly ISmtpSettingsStore _store;

        public EmailService(ISmtpSettingsStore store)
        {
            _store = store;
        }

        public async Task SendAsync(string toEmail, string subject, string bodyText)
        {
            var opt = await _store.GetAsync();

            if (string.IsNullOrWhiteSpace(opt.Host))
                throw new InvalidOperationException("SMTP Host is not configured.");
            if (opt.Port <= 0)
                throw new InvalidOperationException("SMTP Port is not configured.");
            if (string.IsNullOrWhiteSpace(opt.FromEmail))
                throw new InvalidOperationException("SMTP FromEmail is not configured.");
            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("toEmail is required.");

            using var msg = new MailMessage();
            msg.From = new MailAddress(opt.FromEmail, opt.FromName);
            msg.To.Add(toEmail);
            msg.Subject = subject ?? "";
            msg.Body = bodyText ?? "";
            msg.IsBodyHtml = false;

            using var client = new SmtpClient(opt.Host, opt.Port);

            // Encryption options
            // UseSsl typically means implicit SSL (465). UseStartTls typically means STARTTLS (587).
            client.EnableSsl = opt.UseSsl || opt.UseStartTls;

            // Auth if username provided
            if (!string.IsNullOrWhiteSpace(opt.Username))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(opt.Username, opt.Password ?? "");
            }
            else
            {
                client.UseDefaultCredentials = true;
            }

            await client.SendMailAsync(msg);
        }
    }
}
