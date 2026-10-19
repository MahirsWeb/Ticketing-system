using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Signed, expiring download links built on ASP.NET Data Protection (the same persisted key ring that already
/// protects identity cookies and tokens, so nothing new has to be configured or kept secret).
/// The token embeds the file key, so a link for one file cannot be replayed against another.
/// </summary>
public class FileLinkService : IFileLinkService
{
    /// <summary>Long enough that a ticket page left open in a tab keeps working through a working session.</summary>
    internal static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(4);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FileLinkService(IDataProtectionProvider dataProtection, IHttpContextAccessor httpContextAccessor)
    {
        _protector = dataProtection.CreateProtector("TicketSystemTech.FileLinks").ToTimeLimitedDataProtector();
        _httpContextAccessor = httpContextAccessor;
    }

    public string? KeyFrom(string? storedReference) => StoredFileKey.FromReference(storedReference);

    public string? ToDownloadUrl(string? storedReference)
    {
        if (string.IsNullOrWhiteSpace(storedReference)) return storedReference;

        var key = StoredFileKey.FromReference(storedReference);
        if (key is null) return null; // not a file this system stored — never hand it to the browser

        var token = _protector.Protect(key, LinkLifetime);
        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null ? $"{request.Scheme}://{request.Host}" : "";
        return $"{baseUrl}/api/files/{key}?t={Uri.EscapeDataString(token)}";
    }

    public bool IsValid(string key, string? token)
    {
        if (!StoredFileKey.IsSafe(key) || string.IsNullOrEmpty(token)) return false;

        try
        {
            return string.Equals(_protector.Unprotect(token), key, StringComparison.Ordinal);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false; // tampered, signed by another key ring, or expired
        }
    }
}
