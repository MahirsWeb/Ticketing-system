using System.ComponentModel.DataAnnotations;

namespace TicketSystemTech.Api.Contracts;

public record OverdueNotificationSettingsDto(bool NotifyOnSlaBreach, int? ManualOverdueDays);

public record UpdateOverdueNotificationSettingsRequest(
    bool NotifyOnSlaBreach,
    [Range(1, 365)] int? ManualOverdueDays
);
