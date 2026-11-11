using Microsoft.Data.SqlClient;

namespace TicketSystemTech.Api.Startup;

/// <summary>
/// Lets the tools that must not run while the application is running (<c>--rekey-data</c>, <c>--revoke-old-keys</c>) FIND OUT that it is. A running
/// application holds a shared SQL Server application lock, on a connection of its own, for as long as it lives; the tool asks for the same lock
/// exclusively and is refused while any instance anywhere holds it. Counting the connections to the database cannot show this: an application that is
/// idle has none open (the pool is empty), and it will write again with the keys it already has the moment somebody uses it.
/// The lock goes away by itself when the process ends, however it ends. Failing to take it never stops the application: it is protection for a
/// tool, not a condition for running.
/// </summary>
public sealed class RunningInstanceMarker : BackgroundService
{
    public const string Resource = "TicketSystemTech.InstanceRunning";
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(15);

    private readonly IConfiguration _configuration;
    private readonly ILogger<RunningInstanceMarker> _logger;

    public RunningInstanceMarker(IConfiguration configuration, ILogger<RunningInstanceMarker> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var text = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(text)) return;
        var connectionString = new SqlConnectionStringBuilder(text) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);

                await using (var take = connection.CreateCommand())
                {
                    take.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = N'Shared', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;";
                    take.Parameters.AddWithValue("@resource", Resource);
                    var granted = (int)(await take.ExecuteScalarAsync(stoppingToken))!;
                    if (granted < 0)
                    {
                        // A tool holds it exclusively right now: the application is not stopped by that, it simply lets go and tries again later.
                        await Task.Delay(RetryAfterFailure, stoppingToken);
                        continue;
                    }
                }

                // Kept for as long as the connection lives; a heartbeat finds out when it has been dropped, and the loop takes the lock again.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(Heartbeat, stoppingToken);
                    await using var ping = connection.CreateCommand();
                    ping.CommandText = "SELECT 1";
                    await ping.ExecuteScalarAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The marker that tells tools the application is running could not be kept; trying again shortly");
                try
                {
                    await Task.Delay(RetryAfterFailure, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
