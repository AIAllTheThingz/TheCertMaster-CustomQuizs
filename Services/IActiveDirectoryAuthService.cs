namespace QuizAPI.Services
{
    public interface IActiveDirectoryAuthService
    {
        Task<ActiveDirectoryAuthResult?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default);
    }
}
