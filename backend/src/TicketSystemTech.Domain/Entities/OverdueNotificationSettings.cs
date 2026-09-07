using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// Global, admin-configured rule for when an open ticket counts as "overdue" and should email its assignees.
/// A single row is seeded and kept up to date in place — there is only ever one active configuration.
/// </summary>
public class OverdueNotificationSettings : BaseEntity
{
    /// <summary>Notify assignees once a ticket's SLA due date has passed.</summary>
    public bool NotifyOnSlaBreach { get; set; } = true;

    /// <summary>Additionally (or instead), notify once a ticket has been open this many days. Null disables it.</summary>
    public int? ManualOverdueDays { get; set; }
}
