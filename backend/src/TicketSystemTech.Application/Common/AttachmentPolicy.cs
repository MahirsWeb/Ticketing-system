namespace TicketSystemTech.Application.Common;

/// <summary>
/// Single source of truth for which uploaded files are accepted and how they may be served back.
/// Used by the upload endpoints, by the email importer (which previously bypassed the allow-list) and by the
/// download endpoint.
/// </summary>
public static class AttachmentPolicy
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt"
    };

    /// <summary>Content types a browser may render in place. Everything else is forced to download, so an
    /// uploaded .html or .svg can never execute in the application's origin.</summary>
    private static readonly Dictionary<string, string> InlineContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf"
    };

    public static bool IsAllowedExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && AllowedExtensions.Contains(extension);

    /// <summary>The content type to serve for a stored file, or null when it must be sent as a plain download.</summary>
    public static string? InlineContentTypeFor(string? extension) =>
        extension is not null && InlineContentTypes.TryGetValue(extension, out var type) ? type : null;
}
