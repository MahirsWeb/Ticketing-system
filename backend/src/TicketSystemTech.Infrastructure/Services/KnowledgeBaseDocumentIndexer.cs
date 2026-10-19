using System.Text;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.KnowledgeBase;
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
    private readonly IVectorIndex _vectorIndex;
    private readonly IFileStorage _fileStorage;

    public KnowledgeBaseDocumentIndexer(AppDbContext db, IDocumentTextExtractor textExtractor, IEmbeddingService embeddingService, IFileStorage fileStorage, IVectorIndex vectorIndex)
    {
        _vectorIndex = vectorIndex;
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
        if (string.IsNullOrWhiteSpace(text))
        {
            _fileStorage.Delete(fileUrl); // nothing was indexed, so the saved original must not be left behind
            return null;
        }

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
                Embedding = embedding,
                EmbeddingModel = embedding is null ? null : _embeddingService.ModelName
            });
        }
        await _db.SaveChangesAsync(ct);
        _vectorIndex.Invalidate();

        return document;
    }

    public async Task<DocumentReindexResult> ReindexDocumentAsync(Guid documentId, Action<int, int>? progress, CancellationToken ct = default)
    {
        var document = await _db.KnowledgeBaseDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null) return new DocumentReindexResult(false, "The document no longer exists.", 0);

        var key = StoredFileKey.FromReference(document.FileUrl);
        await using var original = key is null ? null : _fileStorage.OpenRead(key);
        if (original is null)
            return new DocumentReindexResult(false,
                "The original file was not kept (it was uploaded before originals were stored), so it cannot be read again. Upload it again instead.", 0);

        var text = await _textExtractor.ExtractTextAsync(document.SourceFileName, original, ct);
        if (string.IsNullOrWhiteSpace(text))
            return new DocumentReindexResult(false, "No text could be extracted from the stored file.", 0);

        var pieces = SplitIntoChunks(text).ToList();
        var embedded = new List<(string Text, float[] Vector)>();
        foreach (var piece in pieces)
        {
            var vector = await _embeddingService.EmbedAsync(piece, ct);
            if (vector is null)
                return new DocumentReindexResult(false,
                    "The embedding service did not answer (check GoogleAi:ApiKey and the quota). The document was left unchanged.", 0);

            embedded.Add((piece, vector));
            progress?.Invoke(embedded.Count, pieces.Count);
        }

        // Swap only now that everything is ready, in one save: either the document has all its new chunks or all its old ones.
        _db.KnowledgeBaseChunks.RemoveRange(await _db.KnowledgeBaseChunks.Where(c => c.DocumentId == documentId).ToListAsync(ct));
        foreach (var (pieceText, vector) in embedded)
        {
            _db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk
            {
                DocumentId = documentId,
                Content = pieceText,
                Embedding = vector,
                EmbeddingModel = _embeddingService.ModelName
            });
        }
        await _db.SaveChangesAsync(ct);
        _vectorIndex.Invalidate();

        return new DocumentReindexResult(true, $"{pieces.Count} chunk(s) rebuilt.", pieces.Count);
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
