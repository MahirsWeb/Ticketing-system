using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Remembers the text of every query the application sends to the database, so that a test can say how many a request costs (T-37: a query
/// for each employee is a cost that grows with the staff, and no figure on the page shows it). Counted by the table a query names, which
/// keeps the figure free of what the background workers of the host ask in the meantime.
/// </summary>
public sealed class CommandLog : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _texts = new();

    public void Clear() => _texts.Clear();

    public int CountNaming(string table) => _texts.Count(t => t.Contains(table, StringComparison.Ordinal));

    /// <summary>Whether ONE command (a batch of statements sent together, which is what one SaveChanges is) names all of these. Says that things were saved together.</summary>
    public bool AnyCommandNamingAll(params string[] parts) => _texts.Any(t => parts.All(p => t.Contains(p, StringComparison.Ordinal)));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        _texts.Enqueue(command.CommandText);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _texts.Enqueue(command.CommandText);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _texts.Enqueue(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _texts.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
