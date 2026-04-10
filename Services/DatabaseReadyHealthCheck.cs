using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;

namespace QuizAPI.Services;

public sealed class DatabaseReadyHealthCheck : IHealthCheck
{
    private readonly QuizDbContext _db;

    public DatabaseReadyHealthCheck(QuizDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
                return HealthCheckResult.Unhealthy("Database connection failed.");

            // Lightweight operational check: ensure the current quiz table is queryable.
            await _db.Quizzes.AsNoTracking().Select(q => q.Id).FirstOrDefaultAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database readiness check failed.", ex);
        }
    }
}
