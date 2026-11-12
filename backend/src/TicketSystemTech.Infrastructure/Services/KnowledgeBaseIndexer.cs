using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public class KnowledgeBaseIndexer : IKnowledgeBaseIndexer
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorIndex _vectorIndex;

    public KnowledgeBaseIndexer(AppDbContext db, IEmbeddingService embeddingService, IVectorIndex vectorIndex)
    {
        _db = db;
        _embeddingService = embeddingService;
        _vectorIndex = vectorIndex;
    }

    public async Task<bool> IndexTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets.AsNoTracking().Include(t => t.Company).FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null) return false;

        // A company that does not want its tickets to go to an outside AI provider has no chunk at all: nothing is embedded or kept.
        if (ticket.Company is { AiIndexingEnabled: false })
        {
            if (await _db.KnowledgeBaseChunks.Where(c => c.TicketId == ticketId).ExecuteDeleteAsync(ct) > 0) _vectorIndex.Invalidate();
            return false;
        }

        var internalNotes = await _db.TicketMessages.AsNoTracking()
            .Where(m => m.TicketId == ticketId && m.Type == MessageType.InternalNote)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.BodyHtml)
            .ToListAsync(ct);

        // The people the text is about: the client, whoever opened or closed the ticket, whoever is on it, whoever wrote a note.
        var authorIds = await _db.TicketMessages.AsNoTracking().Where(m => m.TicketId == ticketId).Select(m => m.AuthorId).Distinct().ToListAsync(ct);
        var assigneeIds = await _db.TicketAssignees.AsNoTracking().Where(a => a.TicketId == ticketId).Select(a => a.UserId).ToListAsync(ct);
        var personIds = authorIds.Concat(assigneeIds).Append(ticket.ClientId)
            .Concat(new[] { ticket.OpenedByUserId, ticket.ClosedByUserId }.OfType<Guid>()).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => personIds.Contains(u.Id)).Select(u => u.FirstName + " " + u.LastName).ToListAsync(ct);

        // Search only ever returns tickets with enough internal documentation (same measure the query uses: the
        // summed length of the notes' stored HTML). Embedding the rest would cost an external API call, send the
        // ticket's text to the provider, and store a vector nothing can ever retrieve.
        if (internalNotes.Sum(n => n.Length) < KnowledgeBaseRules.MinInternalNoteChars) return false;

        var sb = new StringBuilder();
        sb.AppendLine(ticket.Title);
        sb.AppendLine(StripHtml(ticket.Description));
        foreach (var note in internalNotes)
            sb.AppendLine(StripHtml(note));
        if (!string.IsNullOrWhiteSpace(ticket.ResolutionSummary))
            sb.AppendLine(ticket.ResolutionSummary);

        // What is stored is what the search shows and what goes to the AI provider, so it is made safe here, once: an e-mail
        // address, a telephone number or a "password: ..." that somebody typed into a note must not be kept in the knowledge base.
        var content = PiiRedactor.RedactCompany(PiiRedactor.RedactNames(PiiRedactor.Redact(sb.ToString().Trim()), names), ticket.Company?.Name);
        if (content.Length == 0) return false;

        var chunk = await _db.KnowledgeBaseChunks.FirstOrDefaultAsync(c => c.TicketId == ticketId, ct);
        var embedding = await _embeddingService.EmbedAsync(content, EmbeddingPriority.Background, ct);

        if (chunk is null)
        {
            chunk = new KnowledgeBaseChunk { TicketId = ticketId, Content = content };
            _db.KnowledgeBaseChunks.Add(chunk);
        }
        else
        {
            chunk.Content = content;
        }

        // A failed embedding call (rate limit, outage) must not wipe a vector the chunk already has: the text is
        // refreshed, the old vector stays usable until a later run replaces it.
        if (embedding is not null)
        {
            chunk.Embedding = embedding;
            chunk.EmbeddingModel = _embeddingService.ModelName;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another run (the ticket was closed while a reindex was working on it) inserted the chunk first and the
            // unique index rejected this one. Apply our content and vector to the row that won instead of failing.
            // Anything else that made the save fail is a real error.
            if (!await ChunkAlreadyExistsAsync(ticketId, ct)) throw;

            var existing = await _db.KnowledgeBaseChunks.FirstAsync(c => c.TicketId == ticketId, ct);
            existing.Content = content;
            if (embedding is not null)
            {
                existing.Embedding = embedding;
                existing.EmbeddingModel = _embeddingService.ModelName;
            }
            await _db.SaveChangesAsync(ct);
        }

        _vectorIndex.Invalidate();
        return embedding is not null;
    }

    private async Task<bool> ChunkAlreadyExistsAsync(Guid ticketId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        return await _db.KnowledgeBaseChunks.AnyAsync(c => c.TicketId == ticketId, ct);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, "<.*?>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
