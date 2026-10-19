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

    public ExternalServiceException(string service, string userMessage, bool unavailable, Exception? inner = null)
        : base(userMessage, inner)
    {
        Service = service;
        Unavailable = unavailable;
    }
}
