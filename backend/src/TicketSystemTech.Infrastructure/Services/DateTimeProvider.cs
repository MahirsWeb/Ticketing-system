using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
