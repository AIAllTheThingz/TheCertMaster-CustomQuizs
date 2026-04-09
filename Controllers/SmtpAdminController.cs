using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizAPI.Services;
using System.ComponentModel.DataAnnotations;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin/smtp")]
    [Authorize(Roles = "Admin")]
    public class SmtpAdminController : ControllerBase
    {
        private readonly ISmtpSettingsStore _store;
        private readonly IEmailService _email;

        public SmtpAdminController(ISmtpSettingsStore store, IEmailService email)
        {
            _store = store;
            _email = email;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var opt = await _store.GetAsync();

            // Do not return password plaintext to the browser
            return Ok(new
            {
                host = opt.Host,
                port = opt.Port,
                useStartTls = opt.UseStartTls,
                useSsl = opt.UseSsl,
                username = opt.Username,
                passwordSet = !string.IsNullOrWhiteSpace(opt.Password),
                fromEmail = opt.FromEmail,
                fromName = opt.FromName,
                notificationEmail = opt.NotificationEmail
            });
        }

        public sealed class UpdateSmtpRequest
        {
            public string Host { get; set; } = "";
            public int Port { get; set; } = 25;
            public bool UseStartTls { get; set; } = false;
            public bool UseSsl { get; set; } = false;
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public bool KeepExistingPasswordWhenBlank { get; set; } = true;
            public string FromEmail { get; set; } = "";
            public string FromName { get; set; } = "QuizAPI";
            public string NotificationEmail { get; set; } = "";
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] UpdateSmtpRequest req)
        {
            if (req == null) return BadRequest("Request body is required.");
            if (string.IsNullOrWhiteSpace(req.Host)) return BadRequest("Host is required.");
            if (req.Port <= 0) return BadRequest("Port must be > 0.");
            if (string.IsNullOrWhiteSpace(req.FromEmail)) return BadRequest("FromEmail is required.");
            if (!new EmailAddressAttribute().IsValid(req.FromEmail.Trim())) return BadRequest("FromEmail must be a valid email address.");
            if (!string.IsNullOrWhiteSpace(req.NotificationEmail) && !new EmailAddressAttribute().IsValid(req.NotificationEmail.Trim()))
                return BadRequest("NotificationEmail must be a valid email address.");

            var opt = new SmtpOptions
            {
                Host = req.Host.Trim(),
                Port = req.Port,
                UseStartTls = req.UseStartTls,
                UseSsl = req.UseSsl,
                Username = req.Username?.Trim() ?? "",
                Password = req.Password ?? "",
                FromEmail = req.FromEmail.Trim(),
                FromName = req.FromName?.Trim() ?? "QuizAPI",
                NotificationEmail = req.NotificationEmail?.Trim() ?? ""
            };

            await _store.SaveAsync(opt, req.KeepExistingPasswordWhenBlank);
            return Ok(new { message = "SMTP settings saved." });
        }

        public sealed class TestEmailRequest
        {
            public string ToEmail { get; set; } = "";
        }

        [HttpPost("test")]
        public async Task<IActionResult> Test([FromBody] TestEmailRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.ToEmail))
                return BadRequest("ToEmail is required.");
            if (!new EmailAddressAttribute().IsValid(req.ToEmail.Trim()))
                return BadRequest("ToEmail must be a valid email address.");

            await _email.SendAsync(req.ToEmail.Trim(), "QuizAPI SMTP Test", "This is a test email from QuizAPI.");
            return Ok(new { message = "Test email sent." });
        }
    }
}
