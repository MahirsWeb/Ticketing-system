using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>An organizational branch (grana). Tickets and staff are scoped to a branch; only Admin sees across all branches.</summary>
public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Mandatory dedicated email address for this branch (e.g. servis@firma.ba). Clients emailing this address create tickets visible to everyone in the branch.</summary>
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
