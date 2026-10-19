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

    /// <summary>
    /// Re-reads the stored original and rebuilds the document's chunks and embeddings, e.g. after the embedding model or the
    /// text extractor improved. Until now the only way was to delete the document and upload it again. The old chunks are
    /// replaced only once every new chunk has been embedded, so a failure part way leaves the document exactly as it was.
    /// </summary>
    Task<TicketSystemTech.Application.KnowledgeBase.DocumentReindexResult> ReindexDocumentAsync(
        Guid documentId, Action<int, int>? progress, CancellationToken ct = default);
}
