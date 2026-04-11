namespace QuizAPI.Services
{
    public sealed class ActiveDirectoryAuthResult
    {
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public List<string> Groups { get; set; } = new();
        public List<string> Roles { get; set; } = new();
    }
}
