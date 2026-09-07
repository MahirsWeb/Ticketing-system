using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>Admin-managed classification staff pick when opening a ticket. Internal-only — never shown to clients.</summary>
public class TicketCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
