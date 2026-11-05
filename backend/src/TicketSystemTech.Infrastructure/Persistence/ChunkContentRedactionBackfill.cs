using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Infrastructure.Persistence;

/// <summary>
/// Chunks of the knowledge base were stored as typed, with e-mail addresses, telephone numbers and "password: ..." in them (the
/// masking happened only on the way out to the AI provider). New chunks are masked when they are made; this makes the ones that
/// already exist the same. Safe to run on every start: masking text that is already masked changes nothing, so the second run finds
/// nothing to do. The vectors are left as they are (a mask changes a few words, not what the text is about).
/// </summary>
public static class ChunkContentRedactionBackfill
{
    public static async Task<int> RunAsync(AppDbContext db, ILogger logger, int page = 200, CancellationToken ct = default)
    {
        var changed = 0;
        var after = Guid.Empty;

        while (true)
        {
            var rows = await db.KnowledgeBaseChunks.AsNoTracking()
                .Where(c => c.Id.CompareTo(after) > 0)
                .OrderBy(c => c.Id)
                .Take(page)
                .Select(c => new { c.Id, c.Content })
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                var safe = PiiRedactor.Redact(row.Content);
                if (string.Equals(safe, row.Content, StringComparison.Ordinal)) continue;

                await db.KnowledgeBaseChunks.Where(c => c.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Content, safe), ct);
                changed++;
            }
            after = rows[^1].Id;
        }

        if (changed > 0) logger.LogWarning("Knowledge base: masked personal data in {Count} stored chunk(s).", changed);
        return changed;
    }
}
