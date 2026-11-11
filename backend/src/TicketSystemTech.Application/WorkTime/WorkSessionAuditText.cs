namespace TicketSystemTech.Application.WorkTime;

/// <summary>
/// The note an audit row keeps about a work session. A dispute about working hours is about what the time WAS and what it was changed
/// to, so a change records both: "from - to -> from - to". Kept free of the database so it can be tested on plain values.
/// </summary>
public static class WorkSessionAuditText
{
    private static string Span(DateTime started, DateTime? ended) => $"{started:O} - {ended?.ToString("O") ?? "running"}";

    /// <summary>A session as it is (added or deleted).</summary>
    public static string Of(Guid sessionId, Guid userId, DateTime started, DateTime? ended) =>
        $"session {sessionId} of user {userId}: {Span(started, ended)}";

    /// <summary>A session that was changed: what it was, then what it is.</summary>
    public static string Changed(Guid sessionId, Guid userId, DateTime wasStarted, DateTime? wasEnded, DateTime started, DateTime? ended) =>
        $"session {sessionId} of user {userId}: {Span(wasStarted, wasEnded)} -> {Span(started, ended)}";
}
