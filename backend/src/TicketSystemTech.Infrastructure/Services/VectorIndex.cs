using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Singleton cache of the vectors search ranks against. "Eligible" means exactly what the search query used to filter
/// on in SQL: every documentation chunk, plus a ticket chunk once its internal notes reach
/// <see cref="KnowledgeBaseRules.MinInternalNoteChars"/>. Only chunks embedded by the model that is configured now are
/// kept (vectors of another model are not comparable).
///
/// The note-length rule can change without the indexer running (a note is added to a ticket), so besides the explicit
/// <see cref="Invalidate"/> calls the copy expires on its own after <see cref="TimeToLive"/>.
/// </summary>
public sealed class VectorIndex : IVectorIndex
{
    internal static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);

    /// <summary>After an <see cref="Invalidate"/> the copy is reloaded on the next use, but not more often than this:
    /// during a bulk reindex the corpus changes every few seconds, and reloading ~30 MB per search would cost more
    /// than the slightly stale answer it avoids.</summary>
    internal static readonly TimeSpan MinReloadInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VectorIndex> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private IReadOnlyList<VectorEntry>? _entries;
    private DateTime _loadedAtUtc;
    private long _version;
    private long _loadedVersion = -1;

    public VectorIndex(IServiceScopeFactory scopeFactory, ILogger<VectorIndex> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Invalidate() => Interlocked.Increment(ref _version);

    public async Task<IReadOnlyList<VectorEntry>> GetAsync(CancellationToken ct = default)
    {
        if (IsFresh(out var cached)) return cached;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (IsFresh(out cached)) return cached; // another caller reloaded while this one waited

            var versionAtStart = Interlocked.Read(ref _version);
            // The load is shared by everyone waiting on it, so one caller giving up must not abort it for the rest:
            // it runs to completion, and only this caller's wait is cancelled.
            var loaded = await LoadAsync(CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            _entries = loaded;
            _loadedAtUtc = DateTime.UtcNow;
            _loadedVersion = versionAtStart; // an Invalidate() during the load leaves the copy stale, as it should
            return loaded;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private bool IsFresh(out IReadOnlyList<VectorEntry> entries)
    {
        entries = _entries!;
        if (_entries is null) return false;

        var age = DateTime.UtcNow - _loadedAtUtc;
        if (age >= TimeToLive) return false;
        return _loadedVersion == Interlocked.Read(ref _version) || age < MinReloadInterval;
    }

    private async Task<IReadOnlyList<VectorEntry>> LoadAsync(CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var model = scope.ServiceProvider.GetRequiredService<IEmbeddingService>().ModelName;

        var rows = await db.KnowledgeBaseChunks.AsNoTracking()
            .Where(c => c.Embedding != null && (c.EmbeddingModel == null || c.EmbeddingModel == model))
            .Where(c => c.DocumentId != null || (c.TicketId != null &&
                db.TicketMessages
                    .Where(m => m.TicketId == c.TicketId && m.Type == MessageType.InternalNote)
                    .Sum(m => (int?)m.BodyHtml.Length) >= KnowledgeBaseRules.MinInternalNoteChars))
            .Select(c => new { c.Id, c.TicketId, c.DocumentId, DepartmentId = c.Ticket != null ? c.Ticket.DepartmentId : null, c.Embedding })
            .ToListAsync(ct);

        var entries = rows
            .Select(r => new VectorEntry(r.Id, r.TicketId, r.DocumentId, r.DepartmentId, IVectorIndex.Normalize(r.Embedding!)))
            .ToList();
        _logger.LogInformation("Vector index loaded: {Count} chunk(s) in {Ms} ms", entries.Count, clock.ElapsedMilliseconds);
        return entries;
    }
}
