using TicketSystemTech.Domain.Entities;

namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Ingests an uploaded documentation file (Word/Excel/PDF) into the knowledge base: extracts its
/// text, splits it into embeddable chunks, and stores each with its own embedding — so the AI
/// assistant can draw on official documentation, not just ticket history.
/// </summary>
public interface IKnowledgeBaseDocumentIndexer
{
    /// <summary>Returns null if the file type isn't supported or no text could be extracted.</summary>
    Task<KnowledgeBaseDocument?> IndexDocumentAsync(string fileName, Stream content, Guid uploadedByUserId, CancellationToken ct = default);
}
