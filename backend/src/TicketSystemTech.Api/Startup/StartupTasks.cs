using System.Globalization;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.Startup;

/// <summary>
/// Everything the application does to the database before it starts serving: apply migrations, check that the
/// encryption key ring still opens the data, finish one-time conversions, seed the first admin.
///
/// Previously this was an inline block in Program.cs. It ran in every instance at the same moment with no lock, so two
/// instances starting together could both migrate; and a failure threw out of Main before the logger had written
/// anything, which in a restart loop leaves nothing to diagnose. Now:
///  - the work is serialised across instances with a SQL Server application lock;
///  - each step is logged with its duration, and a failure is logged as Fatal by the caller;
///  - <c>Startup:Migrate=false</c> turns it all off (the schema is then only verified, and the app refuses to start on
///    an out-of-date database), so a deployment can run <c>--migrate-only</c> as its own step under a privileged login
///    and run the application itself with a login that cannot change the schema.
/// </summary>
public static class StartupTasks
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!configuration.GetValue("Startup:Migrate", true))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
                throw new InvalidOperationException(
                    $"Startup:Migrate is off but the database is missing {pending.Count} migration(s), starting with {pending[0]}. " +
                    "Run the application once with --migrate-only (using a login that may change the schema) before starting it.");

            // The keys MUST be verified on every start, migrating or not. A wrong Pii:HashKey or a key ring that does not
            // belong to this database would otherwise let the application start "healthy" while every sign-in silently fails
            // with "Invalid email or password". After a --migrate-only step the data is already encrypted, so this only reads
            // (it writes nothing here); it refuses to start when a key does not fit. This closes the gap where Startup:Migrate=false
            // — the mode the README suggests for a server — skipped the check that a normal start performs.
            var verifyProtector = scope.ServiceProvider.GetRequiredService<IPiiProtector>();
            var verifyInProduction = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().IsProduction();
            await Step("Encryption check", logger, () => PiiBackfillMigration.RunAsync(db, verifyProtector, logger, refuseToStartWithPlaintextLeft: verifyInProduction, ct: ct));

            logger.LogInformation("Startup:Migrate is off: the database schema is current and the keys were verified; no other startup tasks were run.");
            return;
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection")!;
        await using var startupLock = await SqlStartupLock.AcquireAsync(connectionString, logger, ct);

        await Step("Database migrations", logger, () => db.Database.MigrateAsync(ct));

        var piiProtector = scope.ServiceProvider.GetRequiredService<IPiiProtector>();
        var inProduction = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().IsProduction();
        await Step("Encryption check", logger, () => PiiBackfillMigration.RunAsync(db, piiProtector, logger, refuseToStartWithPlaintextLeft: inProduction, ct: ct));

        await Step("Outbox bodies", logger, () => OutboxBodyBackfill.RunAsync(db, piiProtector, logger, ct));

        await Step("Embedding format", logger, () => EmbeddingFormatBackfill.RunAsync(db, AiSettings.DefaultGeminiEmbeddingModel, logger, ct));

        await Step("Avatar paths", logger, () => AvatarPathBackfill.RunAsync(db, logger, ct));

        await Step("Knowledge base content", logger, () => ChunkContentRedactionBackfill.RunAsync(db, logger, ct: ct));

        await Step("Seed", logger, () => DbSeeder.SeedAsync(
            db,
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            logger, configuration,
            scope.ServiceProvider.GetRequiredService<ITemporaryPasswordGenerator>(),
            scope.ServiceProvider.GetRequiredService<IOptions<TemporaryPasswordOptions>>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>()));
    }

    private static async Task Step(string name, ILogger logger, Func<Task> work)
    {
        var clock = Stopwatch.StartNew();
        await work();
        logger.LogInformation("Startup step '{Step}' finished in {Ms} ms", name, clock.ElapsedMilliseconds);
    }
}

/// <summary>
/// A SQL Server application lock held on its own connection for as long as the startup tasks run. It lives on the
/// server's <c>master</c> database rather than the application's so it also works the very first time, when the
/// application's database does not exist yet. If that connection is not allowed (a restricted login), startup carries
/// on without the lock and says so: the lock is protection against a race, not a requirement for correctness.
/// </summary>
internal sealed class SqlStartupLock : IAsyncDisposable
{
    private readonly SqlConnection? _connection;
    private readonly string? _resource;

    private SqlStartupLock(SqlConnection? connection, string? resource)
    {
        _connection = connection;
        _resource = resource;
    }

    private const int WaitSeconds = 300;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The connection is handed to the lock that is returned, which closes it when it is disposed; every path that does not return the lock disposes it here.")]
    public static async Task<SqlStartupLock> AcquireAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var resource = "TicketSystemTech.Startup." + builder.InitialCatalog;
        builder.InitialCatalog = "master";
        // Not pooled: closing a pooled connection only returns it to the pool, the SQL Server session stays alive, and a
        // session-owned lock would then be held until the process exits (the second instance would wait out its timeout).
        builder.Pooling = false;

        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = WaitSeconds + 30;
            cmd.CommandText =
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeoutMs; SELECT @r;";
            cmd.Parameters.AddWithValue("@resource", resource);
            cmd.Parameters.AddWithValue("@timeoutMs", WaitSeconds * 1000);

            var waited = Stopwatch.StartNew();
            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (result < 0)
                throw new TimeoutException(
                    "Another instance has been running the startup tasks for more than 5 minutes. Check that instance's log before starting this one.");

            if (waited.ElapsedMilliseconds > 500)
                logger.LogInformation("Waited {Ms} ms for another instance to finish its startup tasks", waited.ElapsedMilliseconds);

            return new SqlStartupLock(connection, resource);
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            logger.LogWarning(ex, "Could not take the startup lock (no access to master?); continuing without it");
            return new SqlStartupLock(null, null);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null) return;

        try
        {
            // Released explicitly as well, so it does not depend on the session ending.
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session'";
            cmd.Parameters.AddWithValue("@resource", _resource);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // The connection is closed right below, which ends the (unpooled) session and frees the lock anyway.
        }

        await _connection.DisposeAsync();
    }
}
