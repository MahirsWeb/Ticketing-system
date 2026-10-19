using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// Remembers that a create request carrying a given Idempotency-Key was already carried out, and what it produced.
/// A client that retries (a timeout, a double click, a failed follow-up upload) is handed the original result
/// instead of creating a second ticket. Written in the same transaction as the resource itself, and unique per
/// (user, scope, key), so two simultaneous identical requests can never both succeed.
/// </summary>
public class IdempotencyRecord : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>What kind of request the key belongs to, e.g. "ticket-create".</summary>
    public string Scope { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>Id of the resource the original request created.</summary>
    public Guid ResourceId { get; set; }
}
