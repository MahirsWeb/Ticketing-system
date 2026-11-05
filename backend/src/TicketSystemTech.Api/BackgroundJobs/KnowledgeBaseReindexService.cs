using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.BackgroundJobs;

/// <summary>
/// Runs the knowledge-base reindex outside any HTTP request. Each ticket is handled in its own scope (its own
/// DbContext), so the change tracker never accumulates thousands of 12 KB vectors — the previous single-context loop
/// got slower with every ticket because EF re-scanned everything it was tracking on each save.
///
/// Only tickets that search could return are processed: past the New/Open stage and with enough internal
/// documentation (<see cref="KnowledgeBaseRules.MinInternalNoteChars"/>). Those without an embedding yet are done
/// newest first, so recent history becomes searchable before old.
/// </summary>
public sealed class KnowledgeBaseReindexService : BackgroundService, IKnowledgeBaseReindexJob
{
    /// <summary>If the embedding provider returns nothing this many times in a row it may well be not configured or out of
    /// quota, and carrying on would just burn through the whole list reporting failures. But five tickets it refuses one by
    /// one (a text it will not take) look the same from here, so before giving up the provider is asked something harmless:
    /// only if it does not answer that either is the run stopped.</summary>
    private const int ConsecutiveFailureLimit = 5;

    /// <summary>What the provider is asked to check that it answers at all.</summary>
    private const string ProviderCheckText = "connection check";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KnowledgeBaseReindexService> _logger;
    private sealed record Request(Guid? DocumentId);
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(1);
    private readonly object _gate = new();
    private ReindexStatus _status = new(ReindexState.Idle, 0, 0, 0, 0, null, null, null);

    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public KnowledgeBaseReindexService(IServiceScopeFactory scopeFactory, ILogger<KnowledgeBaseReindexService> logger, TimeProvider time)
    {
        _time = time;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ReindexStatus GetStatus()
    {
        lock (_gate) return _status;
    }

    public bool TryStart() => Begin(new Request(null), "Looking for tickets that need an embedding…");

    public bool TryStartDocument(Guid documentId) => Begin(new Request(documentId), "Reading the stored document…");

    private bool Begin(Request request, string message)
    {
        lock (_gate)
        {
            if (_status.State == ReindexState.Running) return false;
            _status = new ReindexStatus(ReindexState.Running, 0, 0, 0, 0, UtcNow, null, message);
        }

        _requests.Writer.TryWrite(request);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (request.DocumentId is { } documentId) await RunDocumentAsync(documentId, stoppingToken);
                    else await RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    Update(s => s with { State = ReindexState.Failed, FinishedAtUtc = UtcNow, Message = "Stopped because the application is shutting down. Run it again to continue." });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Knowledge-base reindex failed");
                    Update(s => s with { State = ReindexState.Failed, FinishedAtUtc = UtcNow, Message = "Reindex failed: " + ex.Message });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown while idle
        }
    }

    private async Task RunDocumentAsync(Guid documentId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var indexer = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseDocumentIndexer>();

        var result = await indexer.ReindexDocumentAsync(documentId,
            (done, total) => Update(s => s with { Total = total, Processed = done, Embedded = done, Message = "Embedding the document…" }), ct);

        Update(s => s with
        {
            State = result.Succeeded ? ReindexState.Completed : ReindexState.Failed,
            FinishedAtUtc = UtcNow,
            Message = result.Message
        });
        _logger.LogInformation("Document reindex finished: {Succeeded}, {Message}", result.Succeeded, result.Message);
    }

    /// <summary>
    /// A vector made by another model, or of another size, can never be compared with a question's: search ignores it (and
    /// used to score it 0 without a word). Leaving it in place also made this job believe the ticket was "already embedded"
    /// for ever, so after a change of model nothing was ever redone. So the first step of a reindex is to forget such
    /// vectors on tickets, which makes the selection below pick those tickets up again; the documents that own such chunks
    /// are returned so they can be rebuilt from their stored originals.
    /// </summary>
    private async Task<List<Guid>> ForgetStaleVectorsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var modelId = embedding.ModelName;

        var staleIds = await db.KnowledgeBaseChunks.AsNoTracking()
            .Where(c => c.Embedding != null && c.EmbeddingModel != null && c.EmbeddingModel != modelId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (embedding.Dimensions is { } dimensions)
        {
            var wrongSize = await db.Database
                .SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [KnowledgeBaseChunks] WHERE [EmbeddingBinary] IS NOT NULL AND DATALENGTH([EmbeddingBinary]) <> {dimensions * sizeof(float)}")
                .ToListAsync(ct);
            staleIds = staleIds.Union(wrongSize).ToList();
        }

        if (staleIds.Count == 0) return new List<Guid>();

        var cleared = await db.KnowledgeBaseChunks
            .Where(c => staleIds.Contains(c.Id) && c.TicketId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Embedding, (float[]?)null).SetProperty(c => c.EmbeddingModel, (string?)null), ct);

        var documentIds = await db.KnowledgeBaseChunks.AsNoTracking()
            .Where(c => staleIds.Contains(c.Id) && c.DocumentId != null)
            .Select(c => c.DocumentId!.Value)
            .Distinct()
            .ToListAsync(ct);

        _logger.LogWarning(
            "Knowledge-base reindex: {Tickets} ticket chunk(s) and {Documents} document(s) had vectors that do not match the current model '{Model}' and will be rebuilt",
            cleared, documentIds.Count, modelId);
        return documentIds;
    }

    private async Task<bool> ProviderAnswersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        return await embedding.EmbedAsync(ProviderCheckText, EmbeddingPriority.Background, ct) is not null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var staleDocumentIds = await ForgetStaleVectorsAsync(ct);

        List<Guid> ticketIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ticketIds = await db.Tickets.AsNoTracking()
                .Where(t => t.Status != TicketStatus.New && t.Status != TicketStatus.Open)
                .Where(t => !db.KnowledgeBaseChunks.Any(c => c.TicketId == t.Id && c.Embedding != null))
                .Where(t => db.TicketMessages
                    .Where(m => m.TicketId == t.Id && m.Type == MessageType.InternalNote)
                    .Sum(m => (int?)m.BodyHtml.Length) >= KnowledgeBaseRules.MinInternalNoteChars)
                .OrderByDescending(t => t.ClosedAtUtc ?? t.CreatedAt)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        Update(s => s with { Total = ticketIds.Count, Message = ticketIds.Count == 0 && staleDocumentIds.Count == 0 ? "Nothing to do." : "Embedding tickets…" });
        _logger.LogInformation("Knowledge-base reindex started: {Count} ticket(s) to embed", ticketIds.Count);

        var embedded = 0;
        var failed = 0;
        var consecutiveFailures = 0;
        foreach (var ticketId in ticketIds)
        {
            ct.ThrowIfCancellationRequested();

            bool ok;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                ok = await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(ticketId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Reindex: ticket {TicketId} failed", ticketId);
                ok = false;
            }

            if (ok) { embedded++; consecutiveFailures = 0; } else { failed++; consecutiveFailures++; }
            Update(s => s with { Processed = embedded + failed, Embedded = embedded, Failed = failed });

            if (consecutiveFailures >= ConsecutiveFailureLimit)
            {
                if (await ProviderAnswersAsync(ct))
                {
                    _logger.LogWarning("Reindex: {Limit} tickets in a row were refused, but the provider answers a plain request; carrying on", ConsecutiveFailureLimit);
                    consecutiveFailures = 0;
                    continue;
                }

                Update(s => s with
                {
                    State = ReindexState.Failed,
                    FinishedAtUtc = UtcNow,
                    Message = $"Stopped after {ConsecutiveFailureLimit} failures in a row: the embedding provider returned nothing. " +
                              "Check the AI settings (Ai:Provider, Ai:ApiKey / Ai:Endpoint, Ai:Enabled) and that the quota is not exhausted, then run the reindex again — it continues where it stopped."
                });
                _logger.LogWarning("Knowledge-base reindex aborted after {Limit} consecutive failures", ConsecutiveFailureLimit);
                return;
            }
        }

        // Documents whose vectors came from another model are rebuilt from their stored originals.
        var documentsRebuilt = 0;
        foreach (var documentId in staleDocumentIds)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseDocumentIndexer>()
                .ReindexDocumentAsync(documentId, (_, _) => { }, ct);
            if (result.Succeeded) documentsRebuilt++; else failed++;
        }

        Update(s => s with
        {
            State = ReindexState.Completed,
            FinishedAtUtc = UtcNow,
            Message = failed == 0
                ? (documentsRebuilt == 0 ? "Done." : $"Done ({documentsRebuilt} document(s) rebuilt for the current model).")
                : $"Done, but {failed} item(s) could not be embedded. Run it again to retry them."
        });
        _logger.LogInformation("Knowledge-base reindex finished: {Embedded} embedded, {Failed} failed", embedded, failed);
    }

    private void Update(Func<ReindexStatus, ReindexStatus> change)
    {
        lock (_gate) _status = change(_status);
    }
}
