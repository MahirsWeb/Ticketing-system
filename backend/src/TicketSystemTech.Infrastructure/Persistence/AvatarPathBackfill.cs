using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// Profile pictures used to be stored as an absolute URL built from the Host header of the upload request, so the
/// database remembered whichever host name the uploader happened to use (a test machine, a proxy, a forged header). They are
/// stored as <c>/avatars/{file}</c> now and the address is put in front when a response is written. This rewrites the
/// old rows once; running it again finds nothing to do.
/// </summary>
public static partial class AvatarPathBackfill
{
    [GeneratedRegex(@"^https?://[^/]+(/avatars/[^/?#]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Absolute();

    /// <summary>The relative form of an absolute avatar URL, or null if the value is not one.</summary>
    public static string? ToRelative(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var match = Absolute().Match(url.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    public static async Task<int> RunAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var rows = await db.Users
            .Where(u => u.ProfilePictureUrl != null && u.ProfilePictureUrl.StartsWith("http"))
            .Select(u => new { u.Id, u.ProfilePictureUrl })
            .ToListAsync(ct);

        var changed = 0;
        foreach (var row in rows)
        {
            var relative = ToRelative(row.ProfilePictureUrl);
            if (relative is null) continue;

            await db.Users.Where(u => u.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePictureUrl, relative), ct);
            changed++;
        }

        if (changed > 0) logger.LogInformation("Avatar paths: {Count} profile picture address(es) made host-independent", changed);
        return changed;
    }
}
