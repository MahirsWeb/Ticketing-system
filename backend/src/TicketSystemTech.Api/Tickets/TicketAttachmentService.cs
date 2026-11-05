using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Tickets;

/// <summary>Files attached to a ticket or to one of its messages.</summary>
public interface ITicketAttachmentService
{
    Task<Outcome<List<TicketAttachmentDto>>> UploadAsync(Guid ticketId, Guid? messageId, List<IFormFile>? files);
}

public sealed class TicketAttachmentService : ITicketAttachmentService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IFileLinkService _links;

    public TicketAttachmentService(AppDbContext db, ICurrentUserService currentUser, IFileStorage fileStorage, IFileLinkService links)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _links = links;
    }

    public async Task<Outcome<List<TicketAttachmentDto>>> UploadAsync(Guid ticketId, Guid? messageId, List<IFormFile>? files)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null) return Outcome<List<TicketAttachmentDto>>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<List<TicketAttachmentDto>>.Forbidden();

        if (files is null || files.Count == 0)
            return Outcome<List<TicketAttachmentDto>>.Invalid("No files were provided.");

        // messageId is client-supplied: without this a file could be hung on a message of a different ticket.
        if (messageId.HasValue &&
            !await _db.TicketMessages.AnyAsync(m => m.Id == messageId.Value && m.TicketId == ticketId))
            return Outcome<List<TicketAttachmentDto>>.Invalid("Message does not belong to this ticket.");

        var results = new List<TicketAttachmentDto>();
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName);
            if (!AttachmentPolicy.IsAllowedExtension(extension))
                return Outcome<List<TicketAttachmentDto>>.Invalid($"File type '{extension}' is not allowed.");
            if (file.Length > AttachmentPolicy.MaxFileSizeBytes)
                return Outcome<List<TicketAttachmentDto>>.Invalid($"File '{file.FileName}' exceeds the 10 MB limit.");

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
        return Outcome<List<TicketAttachmentDto>>.Ok(results);
    }
}
