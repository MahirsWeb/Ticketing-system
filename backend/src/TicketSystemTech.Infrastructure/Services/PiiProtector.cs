using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Singleton by design: EF Core caches the model (including value converters built from this instance)
/// for the lifetime of the app, so this must not depend on anything scoped/per-request.
/// </summary>
public class PiiProtector : IPiiProtector
{
    private const string Purpose = "TicketSystemTech.Pii.v1";
    private readonly IDataProtector _protector;
    private readonly byte[] _hashKey;

    public PiiProtector(IDataProtectionProvider dataProtectionProvider, IOptions<PiiOptions> options)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        var hashKey = options.Value.HashKey;
        _hashKey = string.IsNullOrWhiteSpace(hashKey)
            ? Encoding.UTF8.GetBytes("dev-only-insecure-pii-hash-key-set-Pii__HashKey-in-real-envs")
            : Encoding.UTF8.GetBytes(hashKey);
    }

    public string? Encrypt(string? plaintext)
        => string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);

    public string? Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // Not yet-migrated legacy plaintext (or data from before this feature existed) — surface it
            // as-is instead of crashing the request; the next save will encrypt it.
            return ciphertext;
        }
    }

    public string? Hash(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        var normalized = plaintext.Trim().ToUpperInvariant();
        var hash = HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }
}
