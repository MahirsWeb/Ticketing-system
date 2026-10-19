using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Finds a SQL Server the integration tests may create throwaway databases on. Order:
/// the <c>TST_SQL</c> environment variable (what CI sets), then the connection string the developer's own
/// <c>appsettings.Development.json</c> uses (so a plain <c>dotnet test</c> works on a development machine).
/// The database name in either is ignored: every run creates its own <c>TicketSystemTech_AuthzTest_*</c> database and
/// drops it afterwards, and never touches the application's database.
/// </summary>
internal static class TestSql
{
    public const string DatabasePrefix = "TicketSystemTech_AuthzTest_";

    public static string? ServerConnectionString { get; } = Resolve();

    public static string ConnectionStringFor(string database)
    {
        var builder = new SqlConnectionStringBuilder(ServerConnectionString!) { InitialCatalog = database, MultipleActiveResultSets = true };
        return builder.ConnectionString;
    }

    /// <summary>A connection to <c>master</c>, not pooled so that dropping a database is never held up by a pooled session.</summary>
    public static SqlConnection OpenMaster()
    {
        var builder = new SqlConnectionStringBuilder(ServerConnectionString!) { InitialCatalog = "master", Pooling = false };
        var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static string? Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("TST_SQL");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "TicketSystemTech.Api", "appsettings.Development.json");
            if (!File.Exists(candidate)) continue;

            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(candidate));
                if (json.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                    && cs.TryGetProperty("DefaultConnection", out var value)
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();
            }
            catch (JsonException)
            {
                // an unreadable developer file just means "no SQL Server available"
            }
            return null;
        }
        return null;
    }
}

/// <summary>A test that needs a real SQL Server. Without one (no <c>TST_SQL</c>, no developer settings) it is reported as skipped.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (TestSql.ServerConnectionString is null)
            Skip = "No SQL Server for integration tests. Set the TST_SQL environment variable to a connection string (see README).";
    }
}

/// <inheritdoc cref="SqlFactAttribute"/>
public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (TestSql.ServerConnectionString is null)
            Skip = "No SQL Server for integration tests. Set the TST_SQL environment variable to a connection string (see README).";
    }
}
