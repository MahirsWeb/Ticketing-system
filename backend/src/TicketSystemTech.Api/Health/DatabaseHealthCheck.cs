using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Health;

/// <summary>
/// "Can this instance actually serve requests?" The old /api/health returned a constant "ok", so a host's health
/// check (Render, a load balancer, IIS monitoring) saw a healthy app while every request failed because the database
/// was unreachable or the schema was behind.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Limit);

        try
        {
            if (!await _db.Database.CanConnectAsync(timeout.Token))
                return HealthCheckResult.Unhealthy("The database cannot be reached.");

            // With Startup:Migrate=false the schema is migrated by a separate deploy step; an instance started before
            // that step finished must not report ready.
            var pending = await _db.Database.GetPendingMigrationsAsync(timeout.Token);
            return pending.Any()
                ? HealthCheckResult.Unhealthy("The database schema is not up to date.")
                : HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The database did not answer in time.");
        }
        catch (Exception)
        {
            // Never put the exception text in the answer: this endpoint is anonymous.
            return HealthCheckResult.Unhealthy("The database check failed.");
        }
    }
}
