using Ganss.Xss;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Allow-list HTML sanitizer (Ganss.Xss defaults: formatting, lists, tables, links, images; no script, no
/// event handlers, no javascript:/vbscript: URLs). Scoped, because a HtmlSanitizer instance should not be
/// used concurrently while its configuration is being touched; one instance per request is plenty.
/// </summary>
public class HtmlSanitizerService : IHtmlSanitizerService
{
    // Inline images from emails and pasted screenshots arrive as base64 data URIs. Allow raster formats
    // only: data: URIs for anything else (text/html, image/svg+xml, ...) can carry active content.
    private static readonly string[] AllowedImageDataPrefixes =
    {
        "data:image/png", "data:image/jpeg", "data:image/jpg", "data:image/gif", "data:image/webp"
    };

    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerService()
    {
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedSchemes.Add("data");
        _sanitizer.FilterUrl += (_, e) =>
        {
            if (!e.OriginalUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

            var isImage = string.Equals(e.Tag.LocalName, "img", StringComparison.OrdinalIgnoreCase);
            var isAllowedRaster = AllowedImageDataPrefixes.Any(p => e.OriginalUrl.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!(isImage && isAllowedRaster)) e.SanitizedUrl = null;
        };
    }

    public string Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? string.Empty : _sanitizer.Sanitize(html);
}
