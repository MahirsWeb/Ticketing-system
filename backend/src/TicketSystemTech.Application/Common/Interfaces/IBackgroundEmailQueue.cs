namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Hands an email to a background worker and returns at once. Used where the time it takes to send must not be visible
/// to the caller: <c>forgot-password</c> sends mail only for accounts that exist, so awaiting the send made "this email is
/// registered" measurable from the response time alone.
/// </summary>
public interface IBackgroundEmailQueue
{
    /// <summary>Queues the message. Never throws and never blocks; if the queue is full the message is dropped and logged.</summary>
    void Enqueue(string toEmail, string subject, string htmlBody);
}
