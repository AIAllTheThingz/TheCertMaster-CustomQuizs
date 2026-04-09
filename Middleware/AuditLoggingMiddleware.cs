namespace QuizAPI.Middleware
{
    public sealed class AuditLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuditLoggingMiddleware> _logger;

        public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var shouldAudit =
                path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/users", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/import", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/files", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/preemployment/config", StringComparison.OrdinalIgnoreCase);

            if (!shouldAudit)
            {
                await _next(context);
                return;
            }

            await _next(context);

            var userName = context.User?.Identity?.IsAuthenticated == true
                ? (context.User.Identity?.Name ?? "authenticated-user")
                : "anonymous";

            _logger.LogInformation(
                "Audit {Method} {Path} by {User} returned {StatusCode} from {RemoteIp}.",
                context.Request.Method,
                path,
                userName,
                context.Response.StatusCode,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
    }
}
