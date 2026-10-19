using System.Text.RegularExpressions;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Storage keys are a GUID plus an optional short alphanumeric extension and nothing else. Anything that does not
/// match this shape is rejected before it is ever combined with a directory path, which rules out traversal
/// (<c>..\</c>, absolute paths, alternate data streams) regardless of what ends up in the database.
/// </summary>
internal static partial class StoredFileKey
{
    [GeneratedRegex(@"^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}(\.[a-z0-9]{1,10})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    public static bool IsSafe(string? key) => key is not null && Shape().IsMatch(key);

    public static string NewFor(string fileName)
    {
        var raw = Path.GetExtension(fileName);
        var extension = new string((raw ?? string.Empty).TrimStart('.').Where(char.IsAsciiLetterOrDigit).Take(10).ToArray());
        return extension.Length == 0 ? Guid.NewGuid().ToString() : $"{Guid.NewGuid()}.{extension}";
    }

    /// <summary>The key of a stored reference: its last path segment, without any query string.</summary>
    public static string? FromReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var path = reference;
        var q = path.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) path = path[..q];
        var slash = path.LastIndexOfAny(new[] { '/', '\\' });
        var key = slash >= 0 ? path[(slash + 1)..] : path;
        return IsSafe(key) ? key : null;
    }
}
