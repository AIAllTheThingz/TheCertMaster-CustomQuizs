namespace QuizAPI.Services
{
    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 25;
        public bool UseStartTls { get; set; } = false;
        public bool UseSsl { get; set; } = false;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "QuizAPI";
        public string NotificationEmail { get; set; } = "";
    }
}
