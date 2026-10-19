using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:guid}/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IFileLinkService _links;

    public AttachmentsController(AppDbContext db, ICurrentUserService currentUser, IFileStorage fileStorage, IFileLinkService links)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _links = links;
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

        // messageId is client-supplied: without this a file could be hung on a message of a different ticket.
        if (messageId.HasValue &&
            !await _db.TicketMessages.AnyAsync(m => m.Id == messageId.Value && m.TicketId == ticketId))
            return BadRequest(new { message = "Message does not belong to this ticket." });

        var results = new List<TicketAttachmentDto>();
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName);
            if (!AttachmentPolicy.IsAllowedExtension(extension))
                return BadRequest(new { message = $"File type '{extension}' is not allowed." });
            if (file.Length > AttachmentPolicy.MaxFileSizeBytes)
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
            results.Add(new TicketAttachmentDto(attachment.Id, attachment.FileName, _links.ToDownloadUrl(attachment.FileUrl) ?? string.Empty, attachment.FileSizeBytes, attachment.ContentType, attachment.CreatedAt));
        }

        await _db.SaveChangesAsync();
        return Ok(results);
    }

    private bool CanAccess(Domain.Entities.Ticket ticket) => _currentUser.Role switch
    {
        Domain.Enums.UserRole.Admin => true,
        // Branches are isolated, strictly: an employee reaches only their own branch's tickets.
        Domain.Enums.UserRole.Employee => ticket.DepartmentId != null && ticket.DepartmentId == _currentUser.DepartmentId,
        Domain.Enums.UserRole.Client => ticket.ClientId == _currentUser.UserId,
        _ => false
    };
}
