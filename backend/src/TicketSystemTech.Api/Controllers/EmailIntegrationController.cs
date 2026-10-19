using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

/// <summary>
/// Lets staff (Employee only — not Admin) connect and triage their branch's shared mailbox
/// (e.g. servis@firma.ba) into tickets. Every staff member in the branch sees the same inbox and marks.
/// Admin is intentionally excluded per product requirements.
/// </summary>
[ApiController]
[Route("api/email-integration")]
[Authorize(Roles = nameof(UserRole.Employee))]
public class EmailIntegrationController : ControllerBase
{
    private readonly IEmailIntegrationService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly MicrosoftGraphOptions _graphOptions;
    private readonly AppDbContext _db;

    public EmailIntegrationController(
        IEmailIntegrationService emailService, ICurrentUserService currentUser, IOptions<MicrosoftGraphOptions> graphOptions, AppDbContext db)
    {
        _emailService = emailService;
        _currentUser = currentUser;
        _graphOptions = graphOptions.Value;
        _db = db;
    }

    [HttpGet("config")]
    public ActionResult<EmailIntegrationConfigDto> GetConfig() => Ok(new EmailIntegrationConfigDto(_graphOptions.ClientId));

    [HttpGet("status")]
    public async Task<ActionResult<EmailConnectionStatusDto>> GetStatus()
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        var status = await _emailService.GetStatusAsync(_currentUser.DepartmentId!.Value);
        return Ok(new EmailConnectionStatusDto(status.Connected, status.ConnectedEmail));
    }

    [HttpPost("connect")]
    public async Task<ActionResult<EmailConnectionStatusDto>> Connect(ConnectEmailRequest request)
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        try
        {
            var status = await _emailService.ConnectAsync(_currentUser.DepartmentId!.Value, _currentUser.UserId!.Value, request.Code, request.RedirectUri);
            return Ok(new EmailConnectionStatusDto(status.Connected, status.ConnectedEmail));
        }
        catch (ExternalServiceException ex)
        {
            // Written to be shown. Anything else is a genuine fault and goes to the global handler (with a trace id)
            // instead of being echoed to the browser as raw exception text.
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        await _emailService.DisconnectAsync(_currentUser.DepartmentId!.Value);
        return Ok();
    }

    [HttpGet("messages")]
    public async Task<ActionResult<List<InboxMessageSummaryDto>>> ListMessages([FromQuery] int take = 25)
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        try
        {
            var messages = await _emailService.ListInboxAsync(_currentUser.DepartmentId!.Value, take);
            return Ok(messages.Select(m => new InboxMessageSummaryDto(
                m.MessageId, m.Subject, m.FromEmail, m.FromName, m.BodyPreview, m.ReceivedAtUtc,
                m.HasAttachments, m.IsMarked, m.ConvertedTicketId)).ToList());
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "No mailbox connected for your branch." });
        }
    }

    [HttpPost("messages/{messageId}/mark")]
    public async Task<IActionResult> SetMarked(string messageId, SetMarkedRequest request)
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        var marked = await _emailService.SetMarkedAsync(_currentUser.DepartmentId!.Value, _currentUser.UserId!.Value, messageId, request.Marked);
        return Ok(new { marked });
    }

    [HttpGet("messages/{messageId}/prefill")]
    public async Task<ActionResult<EmailPrefillDto>> GetPrefill(string messageId)
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        try
        {
            var detail = await _emailService.GetMessageAsync(_currentUser.DepartmentId!.Value, messageId);
            return Ok(new EmailPrefillDto(
                detail.MessageId, detail.FromEmail, detail.Subject, detail.BodyHtml,
                detail.ReceivedAtUtc, detail.Attachments.Count > 0));
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "No mailbox connected for your branch." });
        }
    }

    /// <summary>Called after a ticket is created from a marked email: copies the email's attachments onto the ticket and marks it converted.</summary>
    [HttpPost("messages/{messageId}/complete-ticket")]
    public async Task<IActionResult> CompleteTicket(string messageId, [FromQuery] Guid ticketId)
    {
        if (RequireDepartment() is BadRequestObjectResult noBranch) return noBranch;
        var departmentId = _currentUser.DepartmentId!.Value;

        // The ticket id comes from the query string: without this check any branch member could attach their
        // mailbox's files to (and mark as converted) a ticket belonging to a different branch.
        var ticket = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => new { t.DepartmentId })
            .FirstOrDefaultAsync();
        if (ticket is null) return NotFound();
        if (ticket.DepartmentId is not null && ticket.DepartmentId != departmentId) return Forbid();

        await _emailService.ImportAttachmentsToTicketAsync(departmentId, _currentUser.UserId!.Value, messageId, ticketId);
        await _emailService.MarkConvertedAsync(departmentId, messageId, ticketId);
        return Ok();
    }

    private IActionResult? RequireDepartment() =>
        _currentUser.DepartmentId is null
            ? BadRequest(new { message = "You are not assigned to a branch yet. Ask an admin to assign you to one." })
            : null;
}
