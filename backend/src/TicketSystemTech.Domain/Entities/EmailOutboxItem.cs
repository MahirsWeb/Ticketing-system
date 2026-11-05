using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

public enum EmailOutboxStatus
{
    /// <summary>Waiting to be sent, or waiting for the next attempt after a failure that may pass.</summary>
    Pending = 0,
    Sent = 1,
    /// <summary>Deliberately not sent: the recipient's account is inactive, or e-mail is not configured. Not retried.</summary>
    Skipped = 2,
    /// <summary>Gave up: the provider refused it for good, or every attempt failed.</summary>
    Failed = 3
}

/// <summary>
/// An e-mail that has been promised and not yet (or only just) delivered. The application writes the message here and
/// carries on; a background worker sends it, and tries again later if the provider is down. Before this existed an e-mail was
/// sent while the request was still open, and one that did not go out was simply lost.
/// </summary>
public class EmailOutboxItem : BaseEntity
{
    /// <summary>Stored encrypted, like every other e-mail address.</summary>
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;

    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }

    /// <summary>While a worker is sending this message nobody else picks it up; if that worker dies the lease simply runs out.</summary>
    public DateTime? LeasedUntilUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
}
