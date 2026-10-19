namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Hands out short-lived, tamper-proof download links for privately stored files. Browsers open attachments
/// through plain links that cannot carry the Authorization header, so the link itself is the credential: it is
/// only ever produced for a user who was already authorised to read the ticket or document, and it expires.
/// </summary>
public interface IFileLinkService
{
    /// <summary>Turns a stored reference (a legacy absolute URL or <c>/uploads/{key}</c>) into a signed download URL.
    /// Returns null/empty unchanged so callers can pass optional values straight through.</summary>
    string? ToDownloadUrl(string? storedReference);

    /// <summary>True when <paramref name="token"/> is a valid, unexpired link for exactly this key.</summary>
    bool IsValid(string key, string? token);

    /// <summary>Extracts the storage key (the last path segment) from a stored reference, or null if it has an unsafe shape.</summary>
    string? KeyFrom(string? storedReference);
}
