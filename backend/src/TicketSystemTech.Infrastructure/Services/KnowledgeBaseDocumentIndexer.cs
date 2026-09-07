using System.Text;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

public class KnowledgeBaseDocumentIndexer : IKnowledgeBaseDocumentIndexer
{
    // Keeps each chunk focused enough for a meaningful embedding, while avoiding one huge manual
    // becoming a single all-or-nothing match — long documents split into several searchable pieces.
    private const int MaxChunkChars = 1500;

    private readonly AppDbContext _db;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IFileStorage _fileStorage;

    public KnowledgeBaseDocumentIndexer(AppDbContext db, IDocumentTextExtractor textExtractor, IEmbeddingService embeddingService, IFileStorage fileStorage)
    {
        _db = db;
        _textExtractor = textExtractor;
        _embeddingService = embeddingService;
        _fileStorage = fileStorage;
    }

    public async Task<KnowledgeBaseDocument?> IndexDocumentAsync(string fileName, Stream content, Guid uploadedByUserId, CancellationToken ct = default)
    {
        if (!_textExtractor.IsSupported(fileName)) return null;

        // The extractor needs to read the stream from the start; save the original file first (from
        // the same position) so staff can open the source document, not just see the AI's summary of it.
        string? fileUrl = null;
        if (content.CanSeek)
        {
            var contentType = ContentTypeForExtension(System.IO.Path.GetExtension(fileName));
            fileUrl = await _fileStorage.SaveAsync(fileName, contentType, content, ct);
            content.Position = 0;
        }

        var text = await _textExtractor.ExtractTextAsync(fileName, content, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;

        var document = new KnowledgeBaseDocument
        {
            Title = System.IO.Path.GetFileNameWithoutExtension(fileName),
            SourceFileName = fileName,
            FileUrl = fileUrl,
            UploadedByUserId = uploadedByUserId
        };
        _db.KnowledgeBaseDocuments.Add(document);
        await _db.SaveChangesAsync(ct);

        foreach (var chunkText in SplitIntoChunks(text))
        {
            var embedding = await _embeddingService.EmbedAsync(chunkText, ct);
            _db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk
            {
                DocumentId = document.Id,
                Content = chunkText,
                Embedding = embedding
            });
        }
        await _db.SaveChangesAsync(ct);

        return document;
    }

    private static string ContentTypeForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static IEnumerable<string> SplitIntoChunks(string text)
    {
        var paragraphs = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        var sb = new StringBuilder();
        foreach (var para in paragraphs)
        {
            if (sb.Length > 0 && sb.Length + para.Length > MaxChunkChars)
            {
                yield return sb.ToString().Trim();
                sb.Clear();
            }
            sb.AppendLine(para);
        }
        if (sb.Length > 0)
            yield return sb.ToString().Trim();
    }
}
