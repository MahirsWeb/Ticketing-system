using TicketSystemTech.Domain.Common;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? TicketId { get; set; }
    public bool IsRead { get; set; } = false;
}
