namespace TicketSystemTech.Application.Common.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
