using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/knowledge-base")]
public class KnowledgeBaseController : ControllerBase
{
    private const long MaxDocumentSizeBytes = 20 * 1024 * 1024; // 20 MB

    private const int MinInternalNoteChars = KnowledgeBaseRules.MinInternalNoteChars;

    /// <summary>"Similar" for the report means the top slice of THIS query's own score range, not a fixed
    /// cosine-similarity cutoff. Measured against real production data: Gemini embedding similarity for
    /// this corpus never drops below ~0.50 even for unrelated tickets and rarely exceeds ~0.75 — a fixed
    /// threshold of 0.50 matched 100% of the ticket base, and even 0.60 matched 93%. Only a threshold
    /// relative to each query's own max/min spread produces a report that means anything.</summary>
    private const double SimilarityReportTopFraction = 0.2;

    private const string StaffSystemInstruction =
        "You are a technical support assistant for Ticket System Tech's internal helpdesk. " +
        "You may ONLY use the information given to you in the context — never your own general " +
        "knowledge, and never information about any other product or system. The context mixes two " +
        "kinds of sources: past resolved tickets, and official product documentation uploaded by the " +
        "team — both are equally valid to draw from. It is ordered with the most detailed sources " +
        "first — weigh those most heavily, but synthesize your answer from the patterns across ALL of " +
        "the sources given to you, not just the first one. If you cannot give one confident, specific " +
        "answer, say so plainly, then on a new line list the ticket numbers from the context that look " +
        "most likely to help (e.g. \"Tickets that might help: #12345, #12346\"). Keep answers short and " +
        "practical, aimed at a support agent trying to resolve a client's issue. Reply in the same " +
        "language the question was asked in.";

    /// <summary>Deliberately never told about tickets or internal notes — the client-facing assistant only
    /// ever sees official documentation, so there is no internal/technical content it could leak by design.</summary>
    private const string ClientSystemInstruction =
        "You are a friendly customer support assistant. You may ONLY use the official help documentation " +
        "given to you in the context — never your own general knowledge, and never information about any " +
        "other product or system. Explain things simply, for an end customer who is not a technician — no " +
        "internal jargon, no technical implementation detail. If the context doesn't clearly answer the " +
        "question, say plainly that you don't have enough information rather than guessing. Reply in the " +
        "same language the question was asked in.";

    private const string ClientNoInfoMessage =
        "Žao nam je, nemamo dovoljno informacija u sistemu za vaš problem. Molimo kontaktirajte vašeg administratora.";

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly IKnowledgeBaseDocumentIndexer _documentIndexer;
    private readonly IEmbeddingService _embeddingService;
    private readonly IFileLinkService _links;
    private readonly IVectorIndex _vectorIndex;
    private readonly IKnowledgeBaseReindexJob _reindexJob;

    /// <summary>
    /// Fires when the browser gives up on the request (the user navigates away, presses "Stop", or the connection
    /// drops). It is handed to the database and to the AI calls below so the server stops working for nobody:
    /// without it an abandoned AI question or Excel export keeps running to the end.
    /// </summary>
    private CancellationToken Ct => HttpContext.RequestAborted;

    public KnowledgeBaseController(
        AppDbContext db,
        ICurrentUserService currentUser,
        IChatCompletionService chatCompletionService,
        IKnowledgeBaseDocumentIndexer documentIndexer,
        IEmbeddingService embeddingService,
        IFileLinkService links,
        IVectorIndex vectorIndex,
        IKnowledgeBaseReindexJob reindexJob)
    {
        _vectorIndex = vectorIndex;
        _reindexJob = reindexJob;
        _db = db;
        _currentUser = currentUser;
        _chatCompletionService = chatCompletionService;
        _documentIndexer = documentIndexer;
        _embeddingService = embeddingService;
        _links = links;
    }

    /// <summary>
    /// Starts rebuilding the embeddings that are missing, in the background, and returns at once. Only tickets search
    /// could actually return are processed (enough internal notes), and tickets that already have an embedding are
    /// skipped, so re-running after a rate-limited or interrupted run only does what is still missing. Progress is
    /// available from <c>reindex-status</c>.
    /// </summary>
    [HttpPost("reindex-all")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public IActionResult ReindexAll()
    {
        if (!_reindexJob.TryStart())
            return Conflict(new { message = "A reindex is already running.", status = _reindexJob.GetStatus() });

        return Accepted(_reindexJob.GetStatus());
    }

    [HttpGet("reindex-status")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public ActionResult<ReindexStatus> ReindexStatusEndpoint() => Ok(_reindexJob.GetStatus());

    /// <summary>
    /// Semantic search over the auto-built knowledge base (ticket descriptions + internal notes + resolutions,
    /// plus any uploaded documentation), ranked by cosine similarity between the query's embedding and each
    /// chunk's stored embedding. Falls back to plain keyword matching when no embedding provider is configured
    /// (no Google AI key) or no chunk has an embedding yet.
    /// </summary>
    [HttpGet("search")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<KnowledgeBaseSearchResultDto>>> Search([FromQuery] string query, [FromQuery] int take = 10)
    {
        var matches = await FindMatchesAsync(query, take, includeTicketSources: true);
        return Ok(matches.Select(ToDto).ToList());
    }

    /// <summary>
    /// AI chat endpoint, strictly grounded in the knowledge base: retrieves the most relevant tickets
    /// and documentation for the question and asks Gemini to answer using only that context. Also returns
    /// a small "how common is this" stat: how many other tickets look similar to the described problem.
    /// </summary>
    [HttpPost("ask")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<KnowledgeBaseAskResponseDto>> Ask(KnowledgeBaseAskRequest request)
    {
        // Computed once and reused below — this question previously got embedded twice per request (once
        // here, once again inside the similar-tickets stat), doubling both the Google AI round-trip and
        // the full-corpus scoring pass on every ask.
        var queryEmbedding = await _embeddingService.EmbedAsync(request.Question, Ct);
        var matches = queryEmbedding is not null
            ? await FindMatchesBySimilarityAsync(queryEmbedding, 50, includeTicketSources: true)
            : await FindMatchesByKeywordAsync(request.Question, 50, includeTicketSources: true);
        var sources = matches.Select(ToDto).ToList();
        var stats = await ComputeSimilarTicketStatsAsync(request.Question, queryEmbedding);

        if (matches.Count == 0)
        {
            return Ok(new KnowledgeBaseAskResponseDto(
                "Nothing relevant was found in the knowledge base yet for this question.", sources,
                stats.SimilarCount, stats.TotalEligible, stats.Percentage));
        }

        var context = string.Join("\n---\n", matches.Select(BuildContextEntry));

        var answer = await _chatCompletionService.AskAsync(StaffSystemInstruction, context, request.Question, Ct);
        var finalAnswer = answer ?? "AI answering isn't configured yet — showing the closest matching sources instead.";

        // Tickets the AI explicitly called out by number (e.g. its "might help" fallback list) are the
        // ones most worth a consultant's attention — surface them first in the sources list.
        var mentionedTicketNumbers = ExtractMentionedTicketNumbers(finalAnswer);
        if (mentionedTicketNumbers.Count > 0)
            sources = sources.OrderByDescending(s => s.TicketNumber != null && mentionedTicketNumbers.Contains(s.TicketNumber)).ToList();

        return Ok(new KnowledgeBaseAskResponseDto(finalAnswer, sources, stats.SimilarCount, stats.TotalEligible, stats.Percentage));
    }

    /// <summary>
    /// Client-facing AI chat. Deliberately never draws on tickets — only on uploaded documentation — so
    /// there is no path for internal notes (fixes via database, SQL, code changes, data migrations, ...)
    /// to ever reach a client. Falls back to a plain "we don't have enough information" message instead
    /// of guessing when the documentation doesn't cover the question.
    /// </summary>
    [HttpPost("ask-client")]
    [Authorize(Roles = nameof(UserRole.Client))]
    [EnableRateLimiting("ai")]
    public async Task<ActionResult<KnowledgeBaseAskResponseDto>> AskClient(KnowledgeBaseAskRequest request)
    {
        var matches = await FindMatchesAsync(request.Question, 20, includeTicketSources: false);

        if (matches.Count == 0)
            return Ok(new KnowledgeBaseAskResponseDto(ClientNoInfoMessage, new List<KnowledgeBaseSearchResultDto>(), 0, 0, 0));

        var context = string.Join("\n---\n", matches.Select(BuildContextEntry));
        var answer = await _chatCompletionService.AskAsync(ClientSystemInstruction, context, request.Question, Ct);
        var finalAnswer = string.IsNullOrWhiteSpace(answer) ? ClientNoInfoMessage : answer;

        // Clients see which help articles were used, never ticket numbers/internal detail.
        var sources = matches.Select(ToDto).ToList();
        return Ok(new KnowledgeBaseAskResponseDto(finalAnswer, sources, 0, 0, 0));
    }

    /// <summary>
    /// Full breakdown for the "how common is this problem" report: every ticket that looks similar to the
    /// described problem (not just the top few shown inline in Ask), plus the count/percentage against the
    /// whole eligible ticket pool, for the dedicated similar-tickets page.
    /// </summary>
    [HttpGet("similar-tickets")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<SimilarTicketsResponseDto>> SimilarTickets([FromQuery] string query)
    {
        var stats = await ComputeSimilarTicketStatsAsync(query, includeAllMatches: true);
        return Ok(new SimilarTicketsResponseDto(
            stats.SimilarCount, stats.TotalEligible, stats.Percentage, stats.Matches.Select(ToDto).ToList()));
    }

    private static string BuildContextEntry(ChunkMatch m) =>
        m.Ticket is not null
            ? $"Ticket #{m.Ticket.TicketNumber} — {m.Ticket.Title}\n{m.Content}" +
              (string.IsNullOrWhiteSpace(m.Ticket.ResolutionSummary) ? "" : $"\nResolution: {m.Ticket.ResolutionSummary}")
            : $"Documentation — {m.Document!.Title}\n{m.Content}";

    private static HashSet<string> ExtractMentionedTicketNumbers(string answer) =>
        // Matches both native "#123456" and migrated "#OST-123456" style ticket numbers.
        Regex.Matches(answer, @"#([A-Za-z0-9\-]+)").Select(m => m.Groups[1].Value).ToHashSet();

    // ---------------- Documentation upload/management ----------------

    /// <summary>Uploads one or more documentation files (Word .docx, Excel .xls/.xlsx, PDF), extracts
    /// their text, and indexes them into the knowledge base so the AI assistant can draw on official
    /// documentation, not just ticket history. Legacy binary .doc is not supported — re-save as .docx first.</summary>
    [HttpPost("documents")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult<List<KnowledgeBaseDocumentDto>>> UploadDocuments([FromForm] List<IFormFile> files)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "No files were provided." });

        var results = new List<KnowledgeBaseDocumentDto>();
        var skipped = new List<string>();
        foreach (var file in files)
        {
            if (file.Length > MaxDocumentSizeBytes)
            {
                skipped.Add($"{file.FileName} (exceeds 20 MB)");
                continue;
            }

            await using var stream = file.OpenReadStream();
            var document = await _documentIndexer.IndexDocumentAsync(file.FileName, stream, _currentUser.UserId!.Value);
            if (document is null)
            {
                skipped.Add(file.FileName);
                continue;
            }

            var chunkCount = await _db.KnowledgeBaseChunks.AsNoTracking().CountAsync(c => c.DocumentId == document.Id);
            results.Add(new KnowledgeBaseDocumentDto(document.Id, document.Title, document.SourceFileName, _links.ToDownloadUrl(document.FileUrl), document.CreatedAt, chunkCount));
        }

        if (results.Count == 0)
            return BadRequest(new { message = "None of the files could be indexed (unsupported type, unreadable, or too large).", skipped });

        return Ok(results);
    }

    [HttpGet("documents")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<KnowledgeBaseDocumentDto>>> ListDocuments()
    {
        var docs = await _db.KnowledgeBaseDocuments.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.Id, d.Title, d.SourceFileName, d.FileUrl, d.CreatedAt, ChunkCount = d.Chunks.Count })
            .ToListAsync(Ct);
        return Ok(docs.Select(d => new KnowledgeBaseDocumentDto(
            d.Id, d.Title, d.SourceFileName, _links.ToDownloadUrl(d.FileUrl), d.CreatedAt, d.ChunkCount)).ToList());
    }

    [HttpDelete("documents/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var doc = await _db.KnowledgeBaseDocuments.FindAsync(id);
        if (doc is null) return NotFound();
        _db.KnowledgeBaseDocuments.Remove(doc); // cascades to its chunks
        await _db.SaveChangesAsync();
        _vectorIndex.Invalidate();
        return NoContent();
    }

    // ---------------- matching ----------------

    /// <summary>A retrieved source, either a resolved ticket or a chunk of uploaded documentation — exactly one of Ticket/Document is set.</summary>
    private record ChunkMatch(Ticket? Ticket, KnowledgeBaseDocument? Document, string Content, int Score);

    /// <summary>
    /// The eligible-chunk filter, pushed all the way into SQL: a document chunk always qualifies; a ticket
    /// chunk only qualifies once its internal notes total at least MinInternalNoteChars. Doing this as a
    /// correlated subquery (rather than loading every chunk and filtering in memory) means disqualified
    /// tickets — most of them, in a mature helpdesk — are never even fetched, let alone embedding-compared.
    /// </summary>
    private IQueryable<KnowledgeBaseChunk> EligibleChunksQuery(bool includeTicketSources)
    {
        var query = _db.KnowledgeBaseChunks.AsNoTracking();
        if (!includeTicketSources)
            return query.Where(c => c.DocumentId != null);

        return query.Where(c => c.DocumentId != null || (c.TicketId != null &&
            _db.TicketMessages
                .Where(m => m.TicketId == c.TicketId && m.Type == MessageType.InternalNote)
                .Sum(m => (int?)m.BodyHtml.Length) >= MinInternalNoteChars));
    }

    private async Task<List<ChunkMatch>> FindMatchesAsync(string query, int take, bool includeTicketSources)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<ChunkMatch>();

        // Prefer real semantic search — embed the query and rank chunks by cosine similarity.
        var queryEmbedding = await _embeddingService.EmbedAsync(query, Ct);
        if (queryEmbedding is not null)
        {
            var semanticMatches = await FindMatchesBySimilarityAsync(queryEmbedding, take, includeTicketSources);
            if (semanticMatches.Count > 0) return semanticMatches;
        }

        // Fallback: no embedding provider configured, or no chunk has an embedding yet.
        return await FindMatchesByKeywordAsync(query, take, includeTicketSources);
    }

    private async Task<List<ChunkMatch>> FindMatchesBySimilarityAsync(float[] queryEmbedding, int take, bool includeTicketSources)
    {
        // Eligibility (has a long-enough internal note, or is a document) is filtered in SQL via
        // EligibleChunksQuery — only chunks that already qualify have their embedding pulled into memory.
        // Content (can be large — full ticket notes/resolution) is left out of this pass and fetched only
        // for the handful of chunks that actually rank, so per-query cost stays roughly flat as the
        // corpus grows instead of scaling with total eligible tickets.
        // The vectors come from an in-memory index (already normalised, rebuilt after changes or a few minutes), so a
        // question costs one dot product per eligible chunk instead of a SQL round trip that parsed ~100 MB of JSON.
        var query = IVectorIndex.Normalize(queryEmbedding);
        var top = (await _vectorIndex.GetAsync(Ct))
            .Where(e => includeTicketSources || e.DocumentId != null)
            .Select(e => new { Id = e.ChunkId, e.TicketId, e.DocumentId, Similarity = IVectorIndex.Dot(query, e.Unit) })
            .OrderByDescending(x => x.Similarity)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();

        if (top.Count == 0) return new List<ChunkMatch>();

        var topIds = top.Select(x => x.Id).ToList();
        var contentById = await _db.KnowledgeBaseChunks.AsNoTracking()
            .Where(c => topIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Content })
            .ToDictionaryAsync(c => c.Id, c => c.Content, Ct);

        var scored = top
            .Select(x => new { x.TicketId, x.DocumentId, Content = contentById[x.Id], x.Similarity })
            // Within the relevant pool, the most substantial source (longest content) is listed first —
            // a proxy for "most thoroughly documented" that works the same way for both source types.
            .OrderByDescending(x => x.Content.Length)
            .ToList();

        return await ResolveSourcesAsync(scored.Select(s => (s.TicketId, s.DocumentId, s.Content,
            Score: (int)MathF.Round(Math.Clamp(s.Similarity, 0f, 1f) * 100))));
    }

    private async Task<List<ChunkMatch>> FindMatchesByKeywordAsync(string query, int take, bool includeTicketSources)
    {
        var keywords = query.ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Distinct()
            .ToList();
        if (keywords.Count == 0) return new List<ChunkMatch>();

        var candidates = await EligibleChunksQuery(includeTicketSources)
            .Select(c => new { c.Content, c.TicketId, c.DocumentId })
            .ToListAsync(Ct);

        if (candidates.Count == 0) return new List<ChunkMatch>();

        var scored = candidates
            .Select(c => new { c.TicketId, c.DocumentId, c.Content, Score = keywords.Count(k => c.Content.Contains(k, StringComparison.OrdinalIgnoreCase)) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(take, 1, 500))
            .OrderByDescending(x => x.Content.Length)
            .ToList();

        return await ResolveSourcesAsync(scored.Select(s => (s.TicketId, s.DocumentId, s.Content, s.Score)));
    }

    /// <summary>Loads the actual Ticket/KnowledgeBaseDocument rows for a scored candidate list, dropping
    /// any ticket that turns out to be Open (untriaged tickets never have resolution content worth
    /// surfacing) and preserving the candidates' incoming order.</summary>
    private async Task<List<ChunkMatch>> ResolveSourcesAsync(IEnumerable<(Guid? TicketId, Guid? DocumentId, string Content, int Score)> scored)
    {
        var list = scored.ToList();
        var ticketIds = list.Where(s => s.TicketId != null).Select(s => s.TicketId!.Value).Distinct().ToList();
        var documentIds = list.Where(s => s.DocumentId != null).Select(s => s.DocumentId!.Value).Distinct().ToList();

        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => ticketIds.Contains(t.Id) && t.Status != TicketStatus.Open)
            .ToDictionaryAsync(t => t.Id, t => t, Ct);
        var documents = await _db.KnowledgeBaseDocuments.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d, Ct);

        var results = new List<ChunkMatch>();
        foreach (var s in list)
        {
            if (s.TicketId is { } ticketId && tickets.TryGetValue(ticketId, out var ticket))
                results.Add(new ChunkMatch(ticket, null, s.Content, s.Score));
            else if (s.DocumentId is { } documentId && documents.TryGetValue(documentId, out var document))
                results.Add(new ChunkMatch(null, document, s.Content, s.Score));
        }
        return results;
    }

    // ---------------- similar-tickets report ----------------

    private record SimilarTicketStats(int SimilarCount, int TotalEligible, double Percentage, List<ChunkMatch> Matches);

    /// <summary>
    /// "How common is this problem" — scores every eligible ticket against the query and counts how many
    /// clear the similarity threshold, against the total eligible pool. Ticket-only (documentation isn't
    /// a "similar problem", it's reference material) so the percentage means what it says.
    /// </summary>
    private async Task<SimilarTicketStats> ComputeSimilarTicketStatsAsync(string query, float[]? precomputedQueryEmbedding = null, bool includeAllMatches = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return new SimilarTicketStats(0, 0, 0, new List<ChunkMatch>());

        var totalEligible = await EligibleChunksQuery(includeTicketSources: true).Where(c => c.TicketId != null).CountAsync(Ct);
        if (totalEligible == 0) return new SimilarTicketStats(0, 0, 0, new List<ChunkMatch>());

        var queryEmbedding = precomputedQueryEmbedding ?? await _embeddingService.EmbedAsync(query, Ct);
        List<ChunkMatch> matches;

        if (queryEmbedding is not null)
        {
            // Embedding is needed for every candidate to score it, but Content (can be large — full
            // ticket notes/resolution) is only fetched for the ones that actually clear the cutoff.
            var unitQuery = IVectorIndex.Normalize(queryEmbedding);
            var rawScores = (await _vectorIndex.GetAsync(Ct))
                .Where(e => e.TicketId != null)
                .Select(e => new { Id = e.ChunkId, e.TicketId, Similarity = IVectorIndex.Dot(unitQuery, e.Unit) })
                .ToList();

            if (rawScores.Count == 0)
            {
                matches = new List<ChunkMatch>();
            }
            else
            {
                var maxSim = rawScores.Max(x => x.Similarity);
                var minSim = rawScores.Min(x => x.Similarity);
                var cutoff = maxSim - (maxSim - minSim) * (float)SimilarityReportTopFraction;

                var above = rawScores.Where(x => x.Similarity >= cutoff).OrderByDescending(x => x.Similarity).ToList();
                var aboveIds = above.Select(x => x.Id).ToList();
                var contentById = await _db.KnowledgeBaseChunks.AsNoTracking()
                    .Where(c => aboveIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Content })
                    .ToDictionaryAsync(c => c.Id, c => c.Content, Ct);

                var scored = above
                    .Select(x => new { x.TicketId, Content = contentById[x.Id], Score = (int)MathF.Round(Math.Clamp(x.Similarity, 0f, 1f) * 100) })
                    .ToList();

                matches = await ResolveSourcesAsync(scored.Select(s => ((Guid?)s.TicketId, (Guid?)null, s.Content, s.Score)));
            }
        }
        else
        {
            var keywords = query.ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .Distinct()
                .ToList();

            var candidates = await EligibleChunksQuery(includeTicketSources: true)
                .Where(c => c.TicketId != null)
                .Select(c => new { c.Content, c.TicketId })
                .ToListAsync(Ct);

            // Keyword fallback "similar" bar: at least half of the query's keywords show up in the ticket.
            var minMatches = Math.Max(1, keywords.Count / 2);
            var scored = candidates
                .Select(c => new { c.TicketId, c.Content, Matched = keywords.Count(k => c.Content.Contains(k, StringComparison.OrdinalIgnoreCase)) })
                .Where(x => x.Matched >= minMatches)
                .OrderByDescending(x => x.Matched)
                .ToList();

            matches = await ResolveSourcesAsync(scored.Select(s => ((Guid?)s.TicketId, (Guid?)null, s.Content, s.Matched)));
        }

        var percentage = totalEligible == 0 ? 0 : Math.Round(100.0 * matches.Count / totalEligible, 1);
        return new SimilarTicketStats(matches.Count, totalEligible, percentage, includeAllMatches ? matches : matches.Take(50).ToList());
    }

    private KnowledgeBaseSearchResultDto ToDto(ChunkMatch m) =>
        m.Ticket is not null
            ? new KnowledgeBaseSearchResultDto("Ticket", m.Ticket.Id, m.Ticket.TicketNumber, m.Ticket.Status.ToString(),
                m.Ticket.ClosedAtUtc, m.Ticket.ResolutionSummary, null, null, m.Ticket.Title, m.Score)
            : new KnowledgeBaseSearchResultDto("Document", null, null, null,
                null, null, m.Document!.Id, _links.ToDownloadUrl(m.Document!.FileUrl), m.Document!.Title, m.Score);
}
