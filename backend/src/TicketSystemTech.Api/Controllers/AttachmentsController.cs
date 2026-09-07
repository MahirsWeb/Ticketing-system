using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:guid}/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt"
    };

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileStorage _fileStorage;

    public AttachmentsController(AppDbContext db, ICurrentUserService currentUser, IFileStorage fileStorage)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<List<TicketAttachmentDto>>> Upload(Guid ticketId, [FromForm] Guid? messageId, [FromForm] List<IFormFile> files)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null) return NotFound();
        if (!CanAccess(ticket)) return Forbid();

        if (files is null || files.Count == 0)
            return BadRequest(new { message = "No files were provided." });

        var results = new List<TicketAttachmentDto>();
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(extension))
                return BadRequest(new { message = $"File type '{extension}' is not allowed." });
            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { message = $"File '{file.FileName}' exceeds the 10 MB limit." });

            await using var stream = file.OpenReadStream();
            var url = await _fileStorage.SaveAsync(file.FileName, file.ContentType, stream);

            var attachment = new TicketAttachment
            {
                TicketId = ticketId,
                MessageId = messageId,
                FileName = file.FileName,
                FileUrl = url,
                FileSizeBytes = file.Length,
                ContentType = file.ContentType,
                UploadedByUserId = _currentUser.UserId!.Value
            };
            _db.TicketAttachments.Add(attachment);
            results.Add(new TicketAttachmentDto(attachment.Id, attachment.FileName, attachment.FileUrl, attachment.FileSizeBytes, attachment.ContentType, attachment.CreatedAt));
        }

        await _db.SaveChangesAsync();
        return Ok(results);
    }

    private bool CanAccess(Domain.Entities.Ticket ticket) => _currentUser.Role switch
    {
        Domain.Enums.UserRole.Admin => true,
        // Branches are isolated: staff can access unrouted (New) tickets for triage, plus tickets routed to their own branch.
        Domain.Enums.UserRole.Employee => ticket.DepartmentId == null || ticket.DepartmentId == _currentUser.DepartmentId,
        Domain.Enums.UserRole.Client => ticket.ClientId == _currentUser.UserId,
        _ => false
    };
}
