namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>Extracts plain text from an uploaded documentation file (Word/Excel/PDF) for indexing
/// into the knowledge base. Returns null for unsupported file types.</summary>
public interface IDocumentTextExtractor
{
    bool IsSupported(string fileName);
    Task<string?> ExtractTextAsync(string fileName, Stream content, CancellationToken ct = default);
}
