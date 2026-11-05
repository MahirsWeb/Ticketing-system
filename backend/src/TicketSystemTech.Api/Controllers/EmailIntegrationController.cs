using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Mailbox;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;

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
    private readonly IMailboxService _mailbox;

    public EmailIntegrationController(IMailboxService mailbox) => _mailbox = mailbox;

    [HttpGet("config")]
    public ActionResult<EmailIntegrationConfigDto> GetConfig() => Ok(_mailbox.Config());

    [HttpGet("status")]
    public async Task<ActionResult<EmailConnectionStatusDto>> GetStatus(CancellationToken ct) =>
        this.ToActionResult(await _mailbox.StatusAsync(ct));

    [HttpPost("connect")]
    public async Task<ActionResult<EmailConnectionStatusDto>> Connect(ConnectEmailRequest request) =>
        this.ToActionResult(await _mailbox.ConnectAsync(request));

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect() =>
        this.ToResult(await _mailbox.DisconnectAsync());

    [HttpGet("messages")]
    public async Task<ActionResult<List<InboxMessageSummaryDto>>> ListMessages(CancellationToken ct, [FromQuery] int take = 25) =>
        this.ToActionResult(await _mailbox.ListMessagesAsync(take, ct));

    [HttpPost("messages/{messageId}/mark")]
    public async Task<IActionResult> SetMarked(string messageId, SetMarkedRequest request) =>
        this.ToResult(await _mailbox.SetMarkedAsync(messageId, request));

    [HttpGet("messages/{messageId}/prefill")]
    public async Task<ActionResult<EmailPrefillDto>> GetPrefill(string messageId, CancellationToken ct) =>
        this.ToActionResult(await _mailbox.PrefillAsync(messageId, ct));

    /// <summary>Called after a ticket is created from a marked email: copies the email's attachments onto the ticket and marks it converted.</summary>
    [HttpPost("messages/{messageId}/complete-ticket")]
    public async Task<IActionResult> CompleteTicket(string messageId, [FromQuery] Guid ticketId) =>
        this.ToResult(await _mailbox.CompleteTicketAsync(messageId, ticketId));
}
