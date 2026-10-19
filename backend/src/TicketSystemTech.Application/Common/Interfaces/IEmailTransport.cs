namespace TicketSystemTech.Application.Common.Interfaces;

public enum EmailSendOutcome
{
    Sent,
    /// <summary>Not sent on purpose and not worth another try (inactive recipient, e-mail not configured).</summary>
    Skipped,
    /// <summary>The provider could not take it right now (down, slow, rate limiting); try again later.</summary>
    Retry,
    /// <summary>The provider refused it for good (rejected address, unverified sender); trying again would not help.</summary>
    Failed
}

public sealed record EmailSendResult(EmailSendOutcome Outcome, string? Detail = null)
{
    public static EmailSendResult Sent() => new(EmailSendOutcome.Sent);
    public static EmailSendResult Skipped(string reason) => new(EmailSendOutcome.Skipped, reason);
    public static EmailSendResult Retry(string reason) => new(EmailSendOutcome.Retry, reason);
    public static EmailSendResult Failed(string reason) => new(EmailSendOutcome.Failed, reason);
}

/// <summary>
/// The wire to the e-mail provider, used by the outbox worker and by nothing else. Everything that wants to send mail uses
/// <see cref="IEmailSender"/>, which only files the message in the outbox; this is what finally delivers it, and it says
/// what happened so the worker knows whether to try again.
/// </summary>
public interface IEmailTransport
{
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
