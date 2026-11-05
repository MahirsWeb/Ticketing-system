using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.KnowledgeBase;

/// <summary>The uploaded documentation: adding it, listing it, removing it.</summary>
public interface IKnowledgeBaseDocuments
{
    /// <summary>Uploads one or more documentation files (Word .docx, Excel .xls/.xlsx, PDF), extracts their text, and indexes them into the knowledge base. Legacy binary .doc is not supported.</summary>
    Task<Outcome<List<KnowledgeBaseDocumentDto>>> UploadAsync(List<IFormFile>? files);

    Task<List<KnowledgeBaseDocumentDto>> ListAsync(CancellationToken ct);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    Task<Outcome<Done>> DeleteAsync(Guid id);
}

public sealed class KnowledgeBaseDocuments : IKnowledgeBaseDocuments
{
    private const long MaxDocumentSizeBytes = 20 * 1024 * 1024; // 20 MB

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IKnowledgeBaseDocumentIndexer _documentIndexer;
    private readonly IFileLinkService _links;
    private readonly IFileStorage _fileStorage;
    private readonly IVectorIndex _vectorIndex;

    public KnowledgeBaseDocuments(
        AppDbContext db, ICurrentUserService currentUser, IKnowledgeBaseDocumentIndexer documentIndexer,
        IFileLinkService links, IFileStorage fileStorage, IVectorIndex vectorIndex)
    {
        _db = db;
        _currentUser = currentUser;
        _documentIndexer = documentIndexer;
        _links = links;
        _fileStorage = fileStorage;
        _vectorIndex = vectorIndex;
    }

    public async Task<Outcome<List<KnowledgeBaseDocumentDto>>> UploadAsync(List<IFormFile>? files)
    {
        if (files is null || files.Count == 0)
            return Outcome<List<KnowledgeBaseDocumentDto>>.Invalid("No files were provided.");

        var results = new List<KnowledgeBaseDocumentDto>();
        var skipped = new List<string>();
        foreach (var file in files)
        {
            if (file.Length > MaxDocumentSizeBytes)
            {
                skipped.Add($"{file.FileName} (exceeds 20 MB)");
                continue;
            }

            await using var stream = file.OpenReadStream();
            var document = await _documentIndexer.IndexDocumentAsync(file.FileName, stream, _currentUser.UserId!.Value);
            if (document is null)
            {
                skipped.Add(file.FileName);
                continue;
            }

            var chunkCount = await _db.KnowledgeBaseChunks.AsNoTracking().CountAsync(c => c.DocumentId == document.Id);
            results.Add(new KnowledgeBaseDocumentDto(document.Id, document.Title, document.SourceFileName, _links.ToDownloadUrl(document.FileUrl), document.CreatedAt, chunkCount));
        }

        if (results.Count == 0)
            return Outcome<List<KnowledgeBaseDocumentDto>>.InvalidBody(new { message = "None of the files could be indexed (unsupported type, unreadable, or too large).", skipped });

        return Outcome<List<KnowledgeBaseDocumentDto>>.Ok(results);
    }

    public async Task<List<KnowledgeBaseDocumentDto>> ListAsync(CancellationToken ct)
    {
        var docs = await _db.KnowledgeBaseDocuments.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.Id, d.Title, d.SourceFileName, d.FileUrl, d.CreatedAt, ChunkCount = d.Chunks.Count })
            .ToListAsync(ct);
        return docs.Select(d => new KnowledgeBaseDocumentDto(
            d.Id, d.Title, d.SourceFileName, _links.ToDownloadUrl(d.FileUrl), d.CreatedAt, d.ChunkCount)).ToList();
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => _db.KnowledgeBaseDocuments.AnyAsync(d => d.Id == id, ct);

    public async Task<Outcome<Done>> DeleteAsync(Guid id)
    {
        var doc = await _db.KnowledgeBaseDocuments.FindAsync(id);
        if (doc is null) return Outcome<Done>.NotFound();
        _db.KnowledgeBaseDocuments.Remove(doc); // cascades to its chunks
        await _db.SaveChangesAsync();
        _vectorIndex.Invalidate();
        await _fileStorage.DeleteAsync(doc.FileUrl); // the stored original goes with it; it used to stay on disk with no owner
        return Outcome<Done>.Ok(Done.Value);
    }
}
