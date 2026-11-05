using System.Net;
using System.Text.RegularExpressions;

namespace TicketSystemTech.Application.Common;

/// <summary>What a piece of editor HTML actually says, as opposed to what it is made of.</summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<img\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Image();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    /// <summary>
    /// True when the HTML shows nothing: no text and no picture. "&lt;p&gt;&lt;/p&gt;" is the obvious case, but a reply that is
    /// only "&lt;p&gt; &lt;/p&gt;" or "&lt;p&gt;&amp;nbsp;&lt;/p&gt;" looks empty to the person who reads it and used to be sent anyway. A
    /// picture alone (a pasted screenshot) is content.
    /// </summary>
    public static bool IsBlank(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;
        if (Image().IsMatch(html)) return false;

        var text = WebUtility.HtmlDecode(Tag().Replace(html, " "));
        // Non-breaking and other invisible spaces count as nothing, the same as an ordinary space.
        return text.All(c => char.IsWhiteSpace(c) || c is '\u00A0' or '\u200B' or '\u200C' or '\u200D' or '\uFEFF');
    }
}
