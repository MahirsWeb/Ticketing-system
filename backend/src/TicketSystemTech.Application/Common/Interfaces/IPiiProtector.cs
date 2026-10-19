namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts personally identifiable data (email, phone, address) at rest, and produces a
/// deterministic hash for fields that still need to be looked up by exact value (e.g. login by email)
/// without storing the plaintext.
/// </summary>
public interface IPiiProtector
{
    string? Encrypt(string? plaintext);

    /// <summary>Decrypts a stored value. Throws <see cref="System.Security.Cryptography.CryptographicException"/> when the value
    /// is ciphertext that the current key ring cannot open; values that are not ciphertext (legacy plaintext) pass through.</summary>
    string? Decrypt(string? ciphertext);

    /// <summary>True when the value looks like ciphertext produced by this protector (as opposed to legacy plaintext).</summary>
    bool IsProtected(string? value);

    string? Hash(string? plaintext);
}
