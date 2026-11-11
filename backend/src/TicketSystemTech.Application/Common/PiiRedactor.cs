using System.Text.RegularExpressions;

namespace TicketSystemTech.Application.Common;

/// <summary>
/// Masks e-mail addresses, phone numbers and credentials in text that is about to leave the system for an outside AI provider.
/// Tickets are written by people, and people paste signatures and phone numbers into them; the model does not need those
/// to find a similar problem, so they stay home.
///
/// This is deliberately narrow. It recognises e-mail addresses, the usual shapes of phone numbers (international, and
/// national numbers starting with 0) and credentials that are written down next to a word that says what they are
/// ("lozinka: ...", "password=...", "token: ...", a bearer token, a JWT). It does NOT recognise names, addresses or free-form
/// identifiers, and a secret that is pasted with no word next to it is not found. It is not a substitute for a data-processing agreement with the provider. It errs on the side of leaving ordinary text alone:
/// dates, times, ticket numbers and IP addresses are not touched.
/// </summary>
public static partial class PiiRedactor
{
    public const string EmailMask = "[email]";
    public const string PhoneMask = "[phone]";
    public const string SecretMask = "[secret]";
    public const string NameMask = "[name]";

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    // +387 61 123 456 · 00387 61/123-456 · +387 (0)61 123 456   and   061 123 456 · 061/123-456 · 033-123-456 · 0612345678.
    // The lookbehind keeps it from starting in the middle of a longer token (a date such as 05/03/2025, a time, an id).
    [GeneratedRegex(
        @"(?<![\w+/.:\-])(?:(?:\+|00)\d{2,3}[ \-/]?(?:\(0\)|0)?\d{1,2}[ \-/]?\d{3}[ \-]?\d{3,4}|0\d{2}[ \-/]?\d{3}[ \-]?\d{3,4})(?![\w])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    // "lozinka: Proba123", "Password = abc", "token:abc" (the word stays, the value goes). Only ':' and '=' count as the link between the
    // word and the value, so "the password has expired" is left alone.
    [GeneratedRegex(
        @"(?<![\w])(?<label>lozink\w*|šifr\w*|sifr\w*|passw\w*|passwd|pwd|pass|pin|tajn\w*|secret|token|api[ _\-]?key|client[ _\-]?secret)(?<link>\s*[:=]\s*)(?<value>[^\s<>""']+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LabelledSecret();

    // "Bearer eyJ..." and a bare JWT (three base64url parts, the first starting with eyJ).
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/\-]{16,}=*|\beyJ[\w\-]+\.[\w\-]+\.[\w\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerOrJwt();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        // E-mail first, so that the digits in an address are not read as a phone number.
        var result = Email().Replace(text, EmailMask);
        result = Phone().Replace(result, PhoneMask);
        result = BearerOrJwt().Replace(result, SecretMask);
        return LabelledSecret().Replace(result, m => m.Groups["label"].Value + m.Groups["link"].Value + SecretMask);
    }

    /// <summary>
    /// Masks the names of the people a text is about (the client, the staff on the ticket, the authors of the notes): each part of a name of at least
    /// three letters, as a whole word, in any case. Names are not found by a pattern, so the caller says whose they are; a name that nobody listed is
    /// left alone. A part that is also an ordinary word ("Ana", "Mara") is masked wherever it stands: over-masking costs a little context, not privacy.
    /// </summary>
    private static readonly char[] NameSeparators = { ' ', '-' };

    public static string RedactNames(string? text, IEnumerable<string?> fullNames)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var parts = fullNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => n!.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
            .Select(p => p.Trim(',', '.', ';'))
            .Where(p => p.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .ToList();
        if (parts.Count == 0) return text;

        var pattern = @"(?<![\p{L}\p{N}])(?:" + string.Join("|", parts.Select(Regex.Escape)) + @")(?![\p{L}\p{N}])";
        return Regex.Replace(text, pattern, NameMask, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
