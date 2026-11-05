using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Api.Tickets;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of the files attached to a ticket; the rules about who may and what is allowed are in <see cref="ITicketAttachmentService"/>.</summary>
[ApiController]
[Route("api/tickets/{ticketId:guid}/attachments")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly ITicketAttachmentService _attachments;

    public AttachmentsController(ITicketAttachmentService attachments) => _attachments = attachments;

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<List<TicketAttachmentDto>>> Upload(Guid ticketId, [FromForm] Guid? messageId, [FromForm] List<IFormFile> files) =>
        this.ToActionResult(await _attachments.UploadAsync(ticketId, messageId, files));
}
