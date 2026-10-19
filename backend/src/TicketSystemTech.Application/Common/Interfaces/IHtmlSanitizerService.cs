namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Sanitizes untrusted rich-text / email HTML against an allow-list before it is stored or returned.
/// The frontend renders ticket and message bodies as raw HTML, so anything that reaches it unsanitized
/// (a client's ticket text, an inbound email body) is a stored-XSS vector against staff and admins.
/// </summary>
public interface IHtmlSanitizerService
{
    /// <summary>Returns the HTML with scripts, event handlers, unsafe URL schemes etc. removed. Null/blank gives an empty string.</summary>
    string Sanitize(string? html);
}
