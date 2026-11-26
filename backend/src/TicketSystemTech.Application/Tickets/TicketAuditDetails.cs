using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Application.Tickets;

/// <summary>
/// The text that goes in the <c>Details</c> of the audit rows of a ticket, and the reading of it. It is in one place because two parts of the
/// application depend on the same words: the services that write the rows, and the feed of events for another application (see
/// docs/integration-api.md), which reads the person and the statuses back out of them. The wording is what is already in the database, so
/// the rows that exist keep being understood; a test holds it fixed. A text that is not understood is not an error: the event is still
/// reported, only without the person or the statuses.
/// </summary>
public static class TicketAuditDetails
{
    public static string Added(Guid userId) => $"Added {userId}";

    public static string Removed(Guid userId) => $"Removed {userId}";

    public static string Opened(Guid assigneeId, Guid? branchId) => $"Assigned to {assigneeId}; branch {branchId}";

    public static string StatusChanged(TicketStatus from, TicketStatus to) => $"{from} -> {to}";

    /// <summary>A transfer between branches (departments, <c>Ticket.DepartmentId</c>; NOT sub-branches). A ticket that had no branch is "-".</summary>
    public static string Transferred(Guid? fromDepartmentId, Guid toDepartmentId) => $"Branch {fromDepartmentId?.ToString() ?? "-"} -> {toDepartmentId}";

    /// <summary>The department a ticket left (null: it had none) and the one it went to. Anything else, including a destination that is not an id, is not understood.</summary>
    public static (Guid? From, Guid To)? Transfer(string? details)
    {
        const string prefix = "Branch ";
        if (details is null || !details.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var parts = details[prefix.Length..].Split(" -> ", 2, StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } || !Guid.TryParse(parts[1], out var to)) return null;
        if (parts[0] == "-") return (null, to);
        return Guid.TryParse(parts[0], out var from) ? (from, to) : null;
    }

    public static Guid? AddedUser(string? details) => GuidAfter(details, "Added ");

    public static Guid? RemovedUser(string? details) => GuidAfter(details, "Removed ");

    public static Guid? OpenedAssignee(string? details) => GuidAfter(details, "Assigned to ");

    public static (TicketStatus From, TicketStatus To)? StatusChange(string? details)
    {
        var parts = details?.Split(" -> ", 2, StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 }) return null;
        return TryStatus(parts[0], out var from) && TryStatus(parts[1], out var to) ? (from, to) : null;
    }

    private static bool TryStatus(string text, out TicketStatus status) =>
        Enum.TryParse(text, ignoreCase: false, out status) && Enum.IsDefined(status) && !char.IsDigit(text[0]);

    private static Guid? GuidAfter(string? details, string prefix)
    {
        if (details is null || !details.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = details[prefix.Length..];
        var end = rest.IndexOf(';');
        if (end >= 0) rest = rest[..end];
        return Guid.TryParse(rest.Trim(), out var id) ? id : null;
    }
}
