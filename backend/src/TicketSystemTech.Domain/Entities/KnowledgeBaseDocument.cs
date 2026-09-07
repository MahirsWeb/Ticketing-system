using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>An uploaded piece of internal documentation ingested into the knowledge base.</summary>
public class KnowledgeBaseDocument : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public Guid UploadedByUserId { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}
