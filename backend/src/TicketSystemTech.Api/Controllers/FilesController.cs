using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// Serves privately stored attachments and knowledge-base documents. Browsers open these through plain links that
/// cannot send the Authorization header, so access is granted by a signed, expiring token that the API only puts
/// into responses the caller was already authorised to receive (see <see cref="IFileLinkService"/>). A request
/// without a valid token is answered with 404, never 403, so the endpoint does not reveal which files exist.
/// </summary>
[ApiController]
[Route("api/files")]
[AllowAnonymous]
public class FilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IFileLinkService _links;

    public FilesController(AppDbContext db, IFileStorage storage, IFileLinkService links)
    {
        _db = db;
        _storage = storage;
        _links = links;
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Download(string key, [FromQuery(Name = "t")] string? token)
    {
        if (!_links.IsValid(key, token)) return NotFound();

        var stream = _storage.OpenRead(key);
        if (stream is null) return NotFound();

        // Never let the browser guess a type: an uploaded .html/.svg must not be rendered in this origin.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, max-age=300";

        var inlineType = AttachmentPolicy.InlineContentTypeFor(Path.GetExtension(key));
        if (inlineType is not null)
            return File(stream, inlineType);

        var downloadName = await FindOriginalNameAsync(key) ?? key;
        return File(stream, "application/octet-stream", downloadName);
    }

    private async Task<string?> FindOriginalNameAsync(string key)
    {
        var name = await _db.TicketAttachments.AsNoTracking()
            .Where(a => a.FileUrl.EndsWith(key))
            .Select(a => a.FileName)
            .FirstOrDefaultAsync();

        return name ?? await _db.KnowledgeBaseDocuments.AsNoTracking()
            .Where(d => d.FileUrl != null && d.FileUrl.EndsWith(key))
            .Select(d => d.SourceFileName)
            .FirstOrDefaultAsync();
    }
}
