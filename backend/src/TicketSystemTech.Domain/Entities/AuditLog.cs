using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }

    /// <summary>The address the request came from (as the application saw it, after the trusted proxy's say), when there was a request.</summary>
    public string? IpAddress { get; set; }

    /// <summary>The reference of the request that caused it: the same 12-character reference the response carried and every log line of
    /// that request has, so a row here leads to the exact trace in the log.</summary>
    public string? CorrelationId { get; set; }
}
