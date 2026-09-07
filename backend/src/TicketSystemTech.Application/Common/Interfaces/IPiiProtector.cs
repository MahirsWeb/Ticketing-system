namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts personally identifiable data (email, phone, address) at rest, and produces a
/// deterministic hash for fields that still need to be looked up by exact value (e.g. login by email)
/// without storing the plaintext.
/// </summary>
public interface IPiiProtector
{
    string? Encrypt(string? plaintext);
    string? Decrypt(string? ciphertext);
    string? Hash(string? plaintext);
}
