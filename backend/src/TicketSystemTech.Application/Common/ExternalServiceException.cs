namespace TicketSystemTech.Application.Common;

/// <summary>
/// A call to an outside service (Microsoft 365, ...) failed in a way the user should hear about in plain words.
/// Without this, a provider outage, a timeout or an open circuit breaker all surfaced as the same anonymous 500 (and
/// the Outlook connect flow even echoed the raw exception text back to the browser). The API answers 502/503 with
/// <see cref="Exception.Message"/>, which is always written to be safe to show.
/// </summary>
public sealed class ExternalServiceException : Exception
{
    public string Service { get; }

    /// <summary>True when the service could not be reached or answered too slowly (worth trying again shortly);
    /// false when it was reached and refused the request (retrying the same thing will not help).</summary>
    public bool Unavailable { get; }

    /// <summary>The mailbox connection was refused for good (token revoked or expired): the answer is 409 with this code, because
    /// what has to change is the state of the branch (connect the mailbox again), not the request.</summary>
    public const string MailboxReconnectRequired = "MAILBOX_RECONNECT_REQUIRED";

    /// <summary>A code the screen can act on, or null for the general "outside service" answer.</summary>
    public string? Code { get; }

    public ExternalServiceException(string service, string userMessage, bool unavailable, Exception? inner = null, string? code = null)
        : base(userMessage, inner)
    {
        Service = service;
        Unavailable = unavailable;
        Code = code;
    }
}
