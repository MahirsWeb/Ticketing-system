namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Keeps the knowledge base up to date. Every ticket's client-reported problem, internal notes
/// written by staff, and final resolution are indexed so future tickets with a similar problem
/// can be found and reused.
/// </summary>
public interface IKnowledgeBaseIndexer
{
    Task IndexTicketAsync(Guid ticketId, CancellationToken ct = default);
}
