using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

public class SlaPlan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int ResolutionTimeHours { get; set; }
    public bool IsActive { get; set; } = true;
}
