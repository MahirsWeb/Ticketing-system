using Microsoft.AspNetCore.Identity;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Identity;

/// <summary>
/// Replaces Identity's default UpperInvariantLookupNormalizer. Since Email/UserName are encrypted at
/// rest, NormalizedEmail/NormalizedUserName can no longer be a plaintext-uppercase copy — that would
/// defeat the encryption. Instead they store a deterministic HMAC hash, so FindByEmailAsync and
/// uniqueness checks still work as an equality lookup without ever storing the plaintext twice.
/// </summary>
public class PiiLookupNormalizer : ILookupNormalizer
{
    private readonly IPiiProtector _piiProtector;

    public PiiLookupNormalizer(IPiiProtector piiProtector)
    {
        _piiProtector = piiProtector;
    }

    public string? NormalizeName(string? name) => _piiProtector.Hash(name);
    public string? NormalizeEmail(string? email) => _piiProtector.Hash(email);
}
