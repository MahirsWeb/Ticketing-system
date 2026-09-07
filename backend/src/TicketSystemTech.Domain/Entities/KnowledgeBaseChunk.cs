using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// A searchable chunk of text with its vector embedding, sourced either from a
/// KnowledgeBaseDocument (uploaded documentation) or from a closed Ticket (resolution history).
/// </summary>
public class KnowledgeBaseChunk : BaseEntity
{
    public Guid? DocumentId { get; set; }
    public KnowledgeBaseDocument? Document { get; set; }

    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Embedding vector (stored as JSON in Infrastructure — see AppDbContext).</summary>
    public float[]? Embedding { get; set; }
}
