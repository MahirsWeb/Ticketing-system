using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
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

    /// <summary>Every ASP.NET Data Protection payload starts with the same magic header, which Base64-encodes to this.
    /// It is how real ciphertext is told apart from legacy plaintext without having to try (and fail) to decrypt.</summary>
    private const string CiphertextPrefix = "CfDJ8";

    /// <summary>Only ever used in Development, so a fresh checkout runs without any setup. Outside Development a
    /// missing key is a startup error: a key that is published in the source code is not a secret, and email
    /// lookup hashes made with it could be brute-forced offline.</summary>
    private const string DevelopmentOnlyHashKey = "dev-only-insecure-pii-hash-key-set-Pii__HashKey-in-real-envs";

    private readonly IDataProtector _protector;
    private readonly byte[] _hashKey;

    public PiiProtector(IDataProtectionProvider dataProtectionProvider, IOptions<PiiOptions> options, IHostEnvironment environment)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);

        var hashKey = options.Value.HashKey;
        if (string.IsNullOrWhiteSpace(hashKey))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "Pii:HashKey is not configured. Set the Pii__HashKey environment variable to a long random secret " +
                    "(at least 32 characters) before starting outside Development.");
            hashKey = DevelopmentOnlyHashKey;
        }

        _hashKey = Encoding.UTF8.GetBytes(hashKey);
    }

    public string? Encrypt(string? plaintext)
        => string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);

    /// <summary>
    /// Fails closed. A value that is real ciphertext but cannot be decrypted (wrong or lost key ring) throws
    /// instead of being returned as if it were the plaintext: otherwise the app would show garbage as a user's
    /// email and the next save would encrypt the garbage over the only copy of the real value.
    /// Values that are not ciphertext at all are legacy plaintext and are returned unchanged.
    /// </summary>
    public string? Decrypt(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;
        if (!IsProtected(ciphertext)) return ciphertext;

        return _protector.Unprotect(ciphertext);
    }

    public bool IsProtected(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(CiphertextPrefix, StringComparison.Ordinal);

    public string? Hash(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        var normalized = plaintext.Trim().ToUpperInvariant();
        var hash = HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }
}
