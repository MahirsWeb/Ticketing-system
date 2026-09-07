using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/knowledge-base")]
public class KnowledgeBaseController : ControllerBase
{
    private const long MaxDocumentSizeBytes = 20 * 1024 * 1024; // 20 MB

    /// <summary>A ticket only qualifies as an AI source once its internal notes carry at least this many
    /// characters — screens out tickets that were never really documented internally.</summary>
    private const int MinInternalNoteChars = 100;

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
    private readonly IKnowledgeBaseIndexer _indexer;
    private readonly IKnowledgeBaseDocumentIndexer _documentIndexer;
    private readonly IEmbeddingService _embeddingService;

    public KnowledgeBaseController(
        AppDbContext db,
        ICurrentUserService currentUser,
        IChatCompletionService chatCompletionService,
        IKnowledgeBaseIndexer indexer,
        IKnowledgeBaseDocumentIndexer documentIndexer,
        IEmbeddingService embeddingService)
    {
        _db = db;
        _currentUser = currentUser;
        _chatCompletionService = chatCompletionService;
        _indexer = indexer;
        _documentIndexer = documentIndexer;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Backfills the knowledge base for tickets that predate KB indexing (e.g. closed before this feature
    /// shipped, or bulk-imported directly into the database). Skips tickets that already have an embedded
    /// chunk, so re-running after a partial/rate-limited run only processes what's still missing instead
    /// of redoing everything from scratch.
    /// </summary>
    [HttpPost("reindex-all")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ReindexAll()
    {
        var allTicketIds = await _db.Tickets.AsNoTracking().Select(t => t.Id).ToListAsync();
        var alreadyEmbedded = await _db.KnowledgeBaseChunks.AsNoTracking()
            .Where(c => c.TicketId != null && c.Embedding != null)
            .Select(c => c.TicketId!.Value)
            .ToListAsync();
        var ticketIds = allTicketIds.Except(alreadyEmbedded).ToList();

        foreach (var id in ticketIds)
            await _indexer.IndexTicketAsync(id);

        return Ok(new { indexed = ticketIds.Count, skipped = allTicketIds.Count - ticketIds.Count });
    }

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
        var matches = await FindMatchesAsync(request.Question, 50, includeTicketSources: true);
        var sources = matches.Select(ToDto).ToList();
        var stats = await ComputeSimilarTicketStatsAsync(request.Question);

        if (matches.Count == 0)
        {
            return Ok(new KnowledgeBaseAskResponseDto(
                "Nothing relevant was found in the knowledge base yet for this question.", sources,
                stats.SimilarCount, stats.TotalEligible, stats.Percentage));
        }

        var context = string.Join("\n---\n", matches.Select(BuildContextEntry));

        var answer = await _chatCompletionService.AskAsync(StaffSystemInstruction, context, request.Question);
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
        var answer = await _chatCompletionService.AskAsync(ClientSystemInstruction, context, request.Question);
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
        Regex.Matches(answer, @"#(\d+)").Select(m => m.Groups[1].Value).ToHashSet();

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
            results.Add(new KnowledgeBaseDocumentDto(document.Id, document.Title, document.SourceFileName, document.FileUrl, document.CreatedAt, chunkCount));
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
            .Select(d => new KnowledgeBaseDocumentDto(d.Id, d.Title, d.SourceFileName, d.FileUrl, d.CreatedAt, d.Chunks.Count))
            .ToListAsync();
        return Ok(docs);
    }

    [HttpDelete("documents/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var doc = await _db.KnowledgeBaseDocuments.FindAsync(id);
        if (doc is null) return NotFound();
        _db.KnowledgeBaseDocuments.Remove(doc); // cascades to its chunks
        await _db.SaveChangesAsync();
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
        var queryEmbedding = await _embeddingService.EmbedAsync(query);
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
        var candidates = await EligibleChunksQuery(includeTicketSources)
            .Where(c => c.Embedding != null)
            .Select(c => new { c.Content, c.TicketId, c.DocumentId, c.Embedding })
            .ToListAsync();

        if (candidates.Count == 0) return new List<ChunkMatch>();

        var scored = candidates
            .Select(c => new { c.TicketId, c.DocumentId, c.Content, Similarity = CosineSimilarity(queryEmbedding, c.Embedding!) })
            .OrderByDescending(x => x.Similarity)
            .Take(Math.Clamp(take, 1, 500))
            // Within the relevant pool, the most substantial source (longest content) is listed first —
            // a proxy for "most thoroughly documented" that works the same way for both source types.
            .OrderByDescending(x => x.Content.Length)
            .ToList();

        return await ResolveSourcesAsync(scored.Select(s => (s.TicketId, s.DocumentId, s.Content,
            Score: (int)MathF.Round(Math.Clamp(s.Similarity, 0f, 1f) * 100))));
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0f;
        return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
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
            .ToListAsync();

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
            .ToDictionaryAsync(t => t.Id, t => t);
        var documents = await _db.KnowledgeBaseDocuments.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d);

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
    private async Task<SimilarTicketStats> ComputeSimilarTicketStatsAsync(string query, bool includeAllMatches = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return new SimilarTicketStats(0, 0, 0, new List<ChunkMatch>());

        var totalEligible = await EligibleChunksQuery(includeTicketSources: true).Where(c => c.TicketId != null).CountAsync();
        if (totalEligible == 0) return new SimilarTicketStats(0, 0, 0, new List<ChunkMatch>());

        var queryEmbedding = await _embeddingService.EmbedAsync(query);
        List<ChunkMatch> matches;

        if (queryEmbedding is not null)
        {
            var candidates = await EligibleChunksQuery(includeTicketSources: true)
                .Where(c => c.TicketId != null && c.Embedding != null)
                .Select(c => new { c.Content, c.TicketId, c.Embedding })
                .ToListAsync();

            var rawScores = candidates
                .Select(c => new { c.TicketId, c.Content, Similarity = CosineSimilarity(queryEmbedding, c.Embedding!) })
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

                var scored = rawScores
                    .Where(x => x.Similarity >= cutoff)
                    .Select(x => new { x.TicketId, x.Content, Score = (int)MathF.Round(Math.Clamp(x.Similarity, 0f, 1f) * 100) })
                    .OrderByDescending(x => x.Score)
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
                .ToListAsync();

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

    private static KnowledgeBaseSearchResultDto ToDto(ChunkMatch m) =>
        m.Ticket is not null
            ? new KnowledgeBaseSearchResultDto("Ticket", m.Ticket.Id, m.Ticket.TicketNumber, m.Ticket.Status.ToString(),
                m.Ticket.ClosedAtUtc, m.Ticket.ResolutionSummary, null, null, m.Ticket.Title, m.Score)
            : new KnowledgeBaseSearchResultDto("Document", null, null, null,
                null, null, m.Document!.Id, m.Document!.FileUrl, m.Document!.Title, m.Score);
}
