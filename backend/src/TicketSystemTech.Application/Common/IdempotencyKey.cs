namespace TicketSystemTech.Application.Common;

/// <summary>Validation of the client-supplied Idempotency-Key header.</summary>
public static class IdempotencyKey
{
    public const string HeaderName = "Idempotency-Key";
    public const int MaxLength = 64;

    /// <summary>
    /// A key is a UUID or similar opaque token: letters, digits, '-' and '_' only, at most 64 characters. Anything
    /// else is rejected rather than stored, since the value ends up in an indexed column and in log lines.
    /// </summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxLength && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
