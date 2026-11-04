using TicketSystemTech.Domain.Entities;

namespace TicketSystemTech.Infrastructure.Persistence;

public static class AuditTrailExtensions
{
    /// <summary>
    /// Adds one row to the audit trail: who (<paramref name="actorId"/>), what (<paramref name="action"/>), to which record, when
    /// (the row's creation time), and, filled in when the row is saved, from which address and which request. The caller
    /// saves: the row is committed together with the change it describes, so neither can exist without the other.
    /// <paramref name="details"/> says what changed; it must never contain a password, a token or the text of a message.
    /// </summary>
    public static void Audit(this AppDbContext db, Guid? actorId, string action, string entityType, Guid? entityId, string? details = null) =>
        db.AuditLogs.Add(new AuditLog { UserId = actorId, Action = action, EntityType = entityType, EntityId = entityId, Details = details });
}
