using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public class KnowledgeBaseIndexer : IKnowledgeBaseIndexer
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddingService;

    public KnowledgeBaseIndexer(AppDbContext db, IEmbeddingService embeddingService)
    {
        _db = db;
        _embeddingService = embeddingService;
    }

    public async Task IndexTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null) return;

        var internalNotes = await _db.TicketMessages.AsNoTracking()
            .Where(m => m.TicketId == ticketId && m.Type == MessageType.InternalNote)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.BodyHtml)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine(ticket.Title);
        sb.AppendLine(StripHtml(ticket.Description));
        foreach (var note in internalNotes)
            sb.AppendLine(StripHtml(note));
        if (!string.IsNullOrWhiteSpace(ticket.ResolutionSummary))
            sb.AppendLine(ticket.ResolutionSummary);

        var content = sb.ToString().Trim();
        if (content.Length == 0) return;

        var chunk = await _db.KnowledgeBaseChunks.FirstOrDefaultAsync(c => c.TicketId == ticketId, ct);
        var embedding = await _embeddingService.EmbedAsync(content, ct);

        if (chunk is null)
        {
            chunk = new KnowledgeBaseChunk { TicketId = ticketId, Content = content, Embedding = embedding };
            _db.KnowledgeBaseChunks.Add(chunk);
        }
        else
        {
            chunk.Content = content;
            chunk.Embedding = embedding;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, "<.*?>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
