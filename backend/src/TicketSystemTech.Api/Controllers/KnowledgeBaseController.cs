using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.KnowledgeBase;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of the knowledge base: it reads the request, hands it to the service that owns the rule and turns the answer into a status code.</summary>
[ApiController]
[Route("api/knowledge-base")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IKnowledgeBaseSearch _search;
    private readonly IKnowledgeBaseDocuments _documents;
    private readonly IKnowledgeBaseReindexJob _reindexJob;

    public KnowledgeBaseController(IKnowledgeBaseSearch search, IKnowledgeBaseDocuments documents, IKnowledgeBaseReindexJob reindexJob)
    {
        _search = search;
        _documents = documents;
        _reindexJob = reindexJob;
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

    /// <summary>Rebuilds one uploaded document's chunks and embeddings from its stored original, in the background (progress is
    /// reported by <c>reindex-status</c>, like the ticket reindex). Previously the only way was deleting and re-uploading it.</summary>
    [HttpPost("documents/{id:guid}/reindex")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ReindexDocument(Guid id, CancellationToken ct)
    {
        if (!await _documents.ExistsAsync(id, ct)) return NotFound();
        if (!_reindexJob.TryStartDocument(id))
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
    [EnableRateLimiting(RateLimitPolicies.AiStaff)]
    public async Task<ActionResult<List<KnowledgeBaseSearchResultDto>>> Search([FromQuery] string query, CancellationToken ct, [FromQuery] int take = 10) =>
        this.ToActionResult(await _search.SearchAsync(query, take, ct));

    /// <summary>
    /// AI chat endpoint, strictly grounded in the knowledge base: retrieves the most relevant tickets
    /// and documentation for the question and asks Gemini to answer using only that context. Also returns
    /// a small "how common is this" stat: how many other tickets look similar to the described problem.
    /// </summary>
    [HttpPost("ask")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    [EnableRateLimiting(RateLimitPolicies.AiStaff)]
    public async Task<ActionResult<KnowledgeBaseAskResponseDto>> Ask(KnowledgeBaseAskRequest request, CancellationToken ct) =>
        this.ToActionResult(await _search.AskAsync(request, ct));

    /// <summary>
    /// Client-facing AI chat. Deliberately never draws on tickets — only on uploaded documentation — so
    /// there is no path for internal notes (fixes via database, SQL, code changes, data migrations, ...)
    /// to ever reach a client. Falls back to a plain "we don't have enough information" message instead
    /// of guessing when the documentation doesn't cover the question.
    /// </summary>
    [HttpPost("ask-client")]
    [Authorize(Roles = nameof(UserRole.Client))]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<ActionResult<KnowledgeBaseAskResponseDto>> AskClient(KnowledgeBaseAskRequest request, CancellationToken ct) =>
        this.ToActionResult(await _search.AskClientAsync(request, ct));

    /// <summary>
    /// Full breakdown for the "how common is this problem" report: every ticket that looks similar to the
    /// described problem (not just the top few shown inline in Ask), plus the count/percentage against the
    /// whole eligible ticket pool, for the dedicated similar-tickets page.
    /// </summary>
    [HttpGet("similar-tickets")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    [EnableRateLimiting(RateLimitPolicies.AiStaff)]
    public async Task<ActionResult<SimilarTicketsResponseDto>> SimilarTickets([FromQuery] string query, CancellationToken ct) =>
        this.ToActionResult(await _search.SimilarTicketsAsync(query, ct));

    // ---------------- Documentation upload/management ----------------

    /// <summary>Uploads one or more documentation files (Word .docx, Excel .xls/.xlsx, PDF), extracts
    /// their text, and indexes them into the knowledge base so the AI assistant can draw on official
    /// documentation, not just ticket history. Legacy binary .doc is not supported — re-save as .docx first.</summary>
    [HttpPost("documents")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult<List<KnowledgeBaseDocumentDto>>> UploadDocuments([FromForm] List<IFormFile> files) =>
        this.ToActionResult(await _documents.UploadAsync(files));

    [HttpGet("documents")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<KnowledgeBaseDocumentDto>>> ListDocuments(CancellationToken ct) =>
        Ok(await _documents.ListAsync(ct));

    [HttpDelete("documents/{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var deleted = await _documents.DeleteAsync(id);
        return deleted.Succeeded ? NoContent() : this.ToResult(deleted);
    }
}
