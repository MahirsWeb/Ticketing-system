namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Keeps the knowledge base up to date. Every ticket's client-reported problem, internal notes
/// written by staff, and final resolution are indexed so future tickets with a similar problem
/// can be found and reused.
/// </summary>
public interface IKnowledgeBaseIndexer
{
    /// <summary>Indexes one ticket. Returns true when the ticket now has an embedding; false when it was skipped
    /// (too little internal documentation to ever be a search result) or the embedding provider gave nothing back.</summary>
    Task<bool> IndexTicketAsync(Guid ticketId, CancellationToken ct = default);
}
