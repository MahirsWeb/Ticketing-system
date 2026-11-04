namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>
/// Asks for a ticket to be (re)indexed for the knowledge base, without waiting for it. Embedding calls an outside service that
/// may take half a minute when it is slow, so closing a ticket or adding a note must not wait for it: the request is answered at
/// once and a background worker does the indexing. A ticket asked for several times before it is done is indexed once.
/// </summary>
public interface IKnowledgeBaseIndexQueue
{
    void Enqueue(Guid ticketId);
}
