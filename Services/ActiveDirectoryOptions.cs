namespace QuizAPI.Services
{
    public sealed class ActiveDirectoryOptions
    {
        public bool Enabled { get; set; }
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
