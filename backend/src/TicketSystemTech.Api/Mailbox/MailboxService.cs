using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Mailbox;

/// <summary>
/// A branch's shared mailbox, as staff of that branch use it: connect it, read it, mark what becomes a ticket and say which ticket it became.
/// Every staff member in the branch sees the same inbox and marks.
/// </summary>
public interface IMailboxService
{
    EmailIntegrationConfigDto Config();

    Task<Outcome<EmailConnectionStatusDto>> StatusAsync(CancellationToken ct);

    Task<Outcome<EmailConnectionStatusDto>> ConnectAsync(ConnectEmailRequest request);

    Task<Outcome<Done>> DisconnectAsync();

    Task<Outcome<List<InboxMessageSummaryDto>>> ListMessagesAsync(int take, CancellationToken ct);

    Task<Outcome<object>> SetMarkedAsync(string messageId, SetMarkedRequest request);

    Task<Outcome<EmailPrefillDto>> PrefillAsync(string messageId, CancellationToken ct);

    /// <summary>Called after a ticket is created from a marked email: copies the email's attachments onto the ticket and marks it converted.</summary>
    Task<Outcome<Done>> CompleteTicketAsync(string messageId, Guid ticketId);
}

public sealed class MailboxService : IMailboxService
{
    private readonly IEmailIntegrationService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly MicrosoftGraphOptions _graphOptions;
    private readonly AppDbContext _db;

    public MailboxService(IEmailIntegrationService emailService, ICurrentUserService currentUser, IOptions<MicrosoftGraphOptions> graphOptions, AppDbContext db)
    {
        _emailService = emailService;
        _currentUser = currentUser;
        _graphOptions = graphOptions.Value;
        _db = db;
    }

    public EmailIntegrationConfigDto Config() => new(_graphOptions.ClientId);

    private const string NoBranch = "You are not assigned to a branch yet. Ask an admin to assign you to one.";

    public async Task<Outcome<EmailConnectionStatusDto>> StatusAsync(CancellationToken ct)
    {
        if (_currentUser.DepartmentId is null) return Outcome<EmailConnectionStatusDto>.Invalid(NoBranch);
        var status = await _emailService.GetStatusAsync(_currentUser.DepartmentId!.Value, ct);
        return Outcome<EmailConnectionStatusDto>.Ok(new EmailConnectionStatusDto(status.Connected, status.ConnectedEmail));
    }

    public async Task<Outcome<EmailConnectionStatusDto>> ConnectAsync(ConnectEmailRequest request)
    {
        if (_currentUser.DepartmentId is null) return Outcome<EmailConnectionStatusDto>.Invalid(NoBranch);

        // The mailbox is shared by every employee of the branch. Replacing it must be a deliberate act (disconnect first,
        // which asks for confirmation and is audited), not a side effect of any colleague running the connect flow again.
        // Checked before the authorization code is spent, so a refusal does not burn the user's sign-in.
        var current = await _emailService.GetStatusAsync(_currentUser.DepartmentId!.Value);
        if (current.Connected)
            return Outcome<EmailConnectionStatusDto>.Status(StatusCodes.Status409Conflict, new { code = "MAILBOX_ALREADY_CONNECTED", message = "This branch already has a mailbox connected. Disconnect it first if you want to connect a different one." });

        try
        {
            var status = await _emailService.ConnectAsync(_currentUser.DepartmentId!.Value, _currentUser.UserId!.Value, request.Code, request.RedirectUri, request.CodeVerifier);
            return Outcome<EmailConnectionStatusDto>.Ok(new EmailConnectionStatusDto(status.Connected, status.ConnectedEmail));
        }
        catch (ExternalServiceException ex)
        {
            // Written to be shown. Anything else is a genuine fault and goes to the global handler (with a trace id)
            // instead of being echoed to the browser as raw exception text.
            return Outcome<EmailConnectionStatusDto>.Status(ex.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest, new { message = ex.Message });
        }
    }

    public async Task<Outcome<Done>> DisconnectAsync()
    {
        if (_currentUser.DepartmentId is null) return Outcome<Done>.Invalid(NoBranch);
        await _emailService.DisconnectAsync(_currentUser.DepartmentId!.Value, _currentUser.UserId!.Value);
        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<Outcome<List<InboxMessageSummaryDto>>> ListMessagesAsync(int take, CancellationToken ct)
    {
        if (_currentUser.DepartmentId is null) return Outcome<List<InboxMessageSummaryDto>>.Invalid(NoBranch);
        var messages = await _emailService.ListInboxAsync(_currentUser.DepartmentId!.Value, take, ct);
        return Outcome<List<InboxMessageSummaryDto>>.Ok(messages.Select(m => new InboxMessageSummaryDto(
            m.MessageId, m.Subject, m.FromEmail, m.FromName, m.BodyPreview, m.ReceivedAtUtc,
            m.HasAttachments, m.IsMarked, m.ConvertedTicketId)).ToList());
    }

    public async Task<Outcome<object>> SetMarkedAsync(string messageId, SetMarkedRequest request)
    {
        if (_currentUser.DepartmentId is null) return Outcome<object>.Invalid(NoBranch);
        var marked = await _emailService.SetMarkedAsync(_currentUser.DepartmentId!.Value, _currentUser.UserId!.Value, messageId, request.Marked);
        return Outcome<object>.Ok(new { marked });
    }

    public async Task<Outcome<EmailPrefillDto>> PrefillAsync(string messageId, CancellationToken ct)
    {
        if (_currentUser.DepartmentId is null) return Outcome<EmailPrefillDto>.Invalid(NoBranch);
        var detail = await _emailService.GetMessageAsync(_currentUser.DepartmentId!.Value, messageId, ct);
        return Outcome<EmailPrefillDto>.Ok(new EmailPrefillDto(
            detail.MessageId, detail.FromEmail, detail.Subject, detail.BodyHtml,
            detail.ReceivedAtUtc, detail.Attachments.Count > 0));
    }

    public async Task<Outcome<Done>> CompleteTicketAsync(string messageId, Guid ticketId)
    {
        if (_currentUser.DepartmentId is null) return Outcome<Done>.Invalid(NoBranch);
        var departmentId = _currentUser.DepartmentId!.Value;

        // The ticket id comes from the query string: without this check any branch member could attach their
        // mailbox's files to (and mark as converted) a ticket belonging to a different branch.
        var ticket = await _db.Tickets.AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => new { t.DepartmentId })
            .FirstOrDefaultAsync();
        if (ticket is null) return Outcome<Done>.NotFound();
        if (ticket.DepartmentId != departmentId) return Outcome<Done>.Forbidden();

        // Saying "this e-mail is now that ticket" a second time (a retry after a dropped connection) must not copy its files onto the
        // ticket again; and an e-mail that already became another ticket is not turned into a second one.
        var convertedTo = await _db.EmailTicketMarks.AsNoTracking()
            .Where(m => m.DepartmentId == departmentId && m.MessageId == messageId)
            .Select(m => m.ConvertedTicketId)
            .FirstOrDefaultAsync();
        if (convertedTo == ticketId) return Outcome<Done>.Ok(Done.Value);
        if (convertedTo.HasValue) return Outcome<Done>.Conflict("This e-mail already became another ticket.");

        await _emailService.ImportAttachmentsAndMarkConvertedAsync(departmentId, _currentUser.UserId!.Value, messageId, ticketId);
        return Outcome<Done>.Ok(Done.Value);
    }
}
