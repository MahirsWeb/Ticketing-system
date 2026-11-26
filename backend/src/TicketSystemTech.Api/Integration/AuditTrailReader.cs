using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Integration;

/// <summary>A transfer of a ticket from one branch (department) to another, as the audit trail has it. A branch of null: the ticket had none.</summary>
public sealed record TransferRow(Guid TicketId, DateTime AtUtc, Guid? FromDepartmentId, Guid ToDepartmentId, Guid? ByUserId);

public interface IAuditTrailReader
{
    /// <summary>The rows of the trail of one ticket that say something about its life (opened, status, assignees, transfer, closed, reopened), oldest first.</summary>
    Task<IReadOnlyList<AuditRow>> ForTicketAsync(Guid ticketId, CancellationToken ct);

    /// <summary>The transfers between branches made in a period (<paramref name="fromUtc"/> inclusive, <paramref name="toUtc"/> exclusive), oldest first. A row whose words are not understood is left out.</summary>
    Task<IReadOnlyList<TransferRow>> TransfersAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed record AuditRow(Guid Id, DateTime AtUtc, string Action, string? Details, Guid? UserId);

/// <summary>
/// Reads the audit trail for the reports. Read only. The trail has an index on (entity, id, time) and on (person, time) but none on the kind of
/// action, so what is asked here by action and period is read from the rows of one kind only.
/// </summary>
public sealed class AuditTrailReader : IAuditTrailReader
{
    /// <summary>The actions of the audit trail of a ticket that belong to its life.</summary>
    public static readonly string[] TicketLifeActions =
    {
        "TicketOpened", "TicketAssigneeAdded", "TicketAssigneeRemoved", "TicketStatusChanged", "TicketTransferred", "TicketClosed", "TicketReopened"
    };

    private readonly AppDbContext _db;

    public AuditTrailReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditRow>> ForTicketAsync(Guid ticketId, CancellationToken ct) =>
        await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Ticket) && a.EntityId == ticketId && TicketLifeActions.Contains(a.Action))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AuditRow(a.Id, a.CreatedAt, a.Action, a.Details, a.UserId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TransferRow>> TransfersAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "TicketTransferred" && a.EntityId != null && a.CreatedAt >= fromUtc && a.CreatedAt < toUtc)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.EntityId, a.CreatedAt, a.Details, a.UserId })
            .ToListAsync(ct);

        var result = new List<TransferRow>(rows.Count);
        foreach (var row in rows)
            if (TicketAuditDetails.Transfer(row.Details) is { } t)
                result.Add(new TransferRow(row.EntityId!.Value, row.CreatedAt, t.From, t.To, row.UserId));
        return result;
    }
}
