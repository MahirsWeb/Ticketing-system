using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// What the rest of the application calls to "send" an e-mail. It does not send anything: it files the message in the
/// outbox table and returns, and the outbox worker delivers it (see <c>EmailOutboxDispatcher</c>). The request is therefore
/// no longer held up by the e-mail provider, and a message survives a provider outage or a restart of the application.
///
/// The row is written with a context of its own, so it neither flushes nor depends on whatever the caller still has pending in
/// its unit of work: callers send e-mail about something they have already saved.
///
/// Like the sender it replaces, it is best-effort towards the caller: if even the outbox row cannot be written, that is logged
/// and the request carries on, because the thing the e-mail is about (a ticket, an assignment) has already been saved.
/// </summary>
public sealed class OutboxEmailSender : IEmailSender
{
    private const int MaxSubjectLength = 500;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OutboxEmailSender> _logger;

    public OutboxEmailSender(IServiceScopeFactory scopes, ILogger<OutboxEmailSender> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A subject built from a long ticket title must not make the insert (and with it the notification) fail.
            var shortSubject = subject.Length <= MaxSubjectLength ? subject : subject[..MaxSubjectLength];
            db.EmailOutbox.Add(new EmailOutboxItem { ToEmail = toEmail, Subject = shortSubject, HtmlBody = htmlBody });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "An e-mail with subject '{Subject}' could not be put in the outbox and will not be sent", subject);
        }
    }
}
