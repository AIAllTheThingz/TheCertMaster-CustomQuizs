namespace QuizAPI.Services
{
    public sealed class NoOpActiveDirectoryAuthService : IActiveDirectoryAuthService
    {
        public Task<ActiveDirectoryAuthResult?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ActiveDirectoryAuthResult?>(null);
        }
    }
}
