namespace QuizAPI.Services
{
    public sealed class ActiveDirectoryOptions
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 389;
        public int SslPort { get; set; } = 636;
        public string Domain { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public string NetBiosDomain { get; set; } = string.Empty;
        public string UserPrincipalSuffix { get; set; } = string.Empty;
        public bool RequireMappedRole { get; set; }
        public string DefaultRole { get; set; } = "User";
        public List<string> AdminGroups { get; set; } = new();
        public List<string> UserGroups { get; set; } = new();
    }
}
