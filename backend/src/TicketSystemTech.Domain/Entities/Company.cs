using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>An external client company whose employees submit tickets.</summary>
public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; } = true;
}
