using System.Text.RegularExpressions;

namespace TicketSystemTech.Application.Common;

/// <summary>
/// Masks e-mail addresses and phone numbers in text that is about to leave the system for an outside AI provider.
/// Tickets are written by people, and people paste signatures and phone numbers into them; the model does not need those
/// to find a similar problem, so they stay home.
///
/// This is deliberately narrow. It recognises e-mail addresses and the usual shapes of phone numbers (international,
/// and national numbers starting with 0). It does NOT recognise names, addresses or free-form identifiers, and it is not a
/// substitute for a data-processing agreement with the provider. It errs on the side of leaving ordinary text alone:
/// dates, times, ticket numbers and IP addresses are not touched.
/// </summary>
public static partial class PiiRedactor
{
    public const string EmailMask = "[email]";
    public const string PhoneMask = "[phone]";

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    // +387 61 123 456 · 00387 61/123-456 · +387 (0)61 123 456   and   061 123 456 · 061/123-456 · 033-123-456 · 0612345678.
    // The lookbehind keeps it from starting in the middle of a longer token (a date such as 05/03/2025, a time, an id).
    [GeneratedRegex(
        @"(?<![\w+/.:\-])(?:(?:\+|00)\d{2,3}[ \-/]?(?:\(0\)|0)?\d{1,2}[ \-/]?\d{3}[ \-]?\d{3,4}|0\d{2}[ \-/]?\d{3}[ \-]?\d{3,4})(?![\w])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        // E-mail first, so that the digits in an address are not read as a phone number.
        var result = Email().Replace(text, EmailMask);
        return Phone().Replace(result, PhoneMask);
    }
}
