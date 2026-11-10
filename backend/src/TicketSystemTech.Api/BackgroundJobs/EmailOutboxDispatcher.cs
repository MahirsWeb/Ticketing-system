using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.BackgroundJobs;

/// <summary>
/// Delivers the e-mail waiting in the outbox. The application only ever files a message there (see
/// <c>OutboxEmailSender</c>); this worker sends it, and tries again later when the provider could not take it.
///
/// Guarantees, and where they stop:
/// <list type="bullet">
/// <item>A message survives a restart and a provider outage, and is tried up to <see cref="EmailRetryPolicy.MaxAttempts"/> times.</item>
/// <item>Two workers (two instances of the application) never send the same message at the same moment: a message is leased
/// with a conditional UPDATE before it is sent, and only the worker whose UPDATE changed a row sends it.</item>
/// <item>Delivery is at least once. If the process dies after the provider accepted a message but before the outcome was
/// written, the lease runs out and the message is sent again. That is the price of never losing one; the outcome is
/// written even while the application is shutting down to keep that window as small as it can be.</item>
/// </list>
/// </summary>
public sealed class EmailOutboxDispatcher : BackgroundService
{
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(1);
    private const int MaxErrorLength = 500;

    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOutboxOptions _options;
    private readonly ILogger<EmailOutboxDispatcher> _logger;

    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public EmailOutboxDispatcher(IServiceScopeFactory scopes, IOptions<EmailOutboxOptions> options, ILogger<EmailOutboxDispatcher> logger, TimeProvider time)
    {
        _time = time;
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("The e-mail outbox worker is switched off (Email:Outbox:Enabled); queued e-mail is not being sent");
            return;
        }

        var lastCleanup = DateTime.MinValue;
        var poll = TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 1, 3600));

        while (!stoppingToken.IsCancellationRequested)
        {
            var more = false;
            try
            {
                var now = UtcNow;
                var handled = await ProcessOnceAsync(now, stoppingToken);
                more = handled >= BatchSize; // a full round usually means more is waiting: do not sleep in between

                if (now - lastCleanup >= CleanupEvery)
                {
                    lastCleanup = now;
                    var removed = await DeleteOldAsync(now, stoppingToken);
                    if (removed > 0) _logger.LogInformation("Removed {Count} old messages from the e-mail outbox", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The e-mail outbox round failed; trying again shortly");
            }

            if (more) continue;
            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private int BatchSize => Math.Clamp(_options.BatchSize, 1, 200);

    /// <summary>One round: sends what is due. Returns how many messages were taken. Public so a test can run a round at a
    /// chosen moment instead of waiting for the timer.</summary>
    public async Task<int> ProcessOnceAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        List<Guid> due;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            due = await db.EmailOutbox
                .Where(m => m.Status == EmailOutboxStatus.Pending
                            && m.NextAttemptAtUtc <= nowUtc
                            && (m.LeasedUntilUtc == null || m.LeasedUntilUtc < nowUtc))
                .OrderBy(m => m.NextAttemptAtUtc)
                .Select(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        var taken = 0;
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            if (await DeliverAsync(id, nowUtc, ct)) taken++;
        }
        return taken;
    }

    /// <summary>Removes what has been finished for long enough. Public for the same reason as <see cref="ProcessOnceAsync"/>.</summary>
    public async Task<int> DeleteOldAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var sentBefore = nowUtc.AddDays(-Math.Max(_options.KeepSentDays, 1));
        var failedBefore = nowUtc.AddDays(-Math.Max(_options.KeepFailedDays, 1));

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.EmailOutbox
            .Where(m => ((m.Status == EmailOutboxStatus.Sent || m.Status == EmailOutboxStatus.Skipped) && m.CompletedAtUtc < sentBefore)
                        || (m.Status == EmailOutboxStatus.Failed && m.CompletedAtUtc < failedBefore))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Takes the message (false if another worker got there first), sends it and records what happened.</summary>
    private async Task<bool> DeliverAsync(Guid id, DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();

        // The lease. The WHERE clause is the whole protection: of two workers running this at once, the database lets only one
        // of the UPDATEs change the row, the other sees 0 rows and moves on.
        DateTime? leaseUntil = nowUtc.AddSeconds(Math.Clamp(_options.LeaseSeconds, 10, 3600));
        var claimed = await db.EmailOutbox
            .Where(m => m.Id == id
                        && m.Status == EmailOutboxStatus.Pending
                        && (m.LeasedUntilUtc == null || m.LeasedUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeasedUntilUtc, leaseUntil), ct);
        if (claimed == 0) return false;

        var item = await db.EmailOutbox.FirstAsync(m => m.Id == id, ct);

        EmailSendResult result;
        try
        {
            // Whatever the transport is (SMTP today, anything tomorrow), an account that has been deactivated no longer receives
            // mail from the application: the rule lives here, not in a particular provider's sender.
            if (await RecipientIsInactiveAsync(scope.ServiceProvider, db, item.ToEmail, ct))
            {
                _logger.LogInformation("Outbox message {OutboxId} skipped — the recipient's account is inactive.", id);
                result = EmailSendResult.Skipped("The recipient's account is inactive.");
            }
            else
            {
                result = await transport.SendAsync(item.ToEmail, item.Subject, item.HtmlBody, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down before an answer: give the message back instead of making it wait for the lease to run out.
            await db.EmailOutbox.Where(m => m.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeasedUntilUtc, (DateTime?)null), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The e-mail transport threw while sending outbox message {OutboxId}", id);
            result = EmailSendResult.Retry(ex.GetType().Name);
        }

        Apply(item, result, nowUtc);

        // Not cancellable: the provider has been told already (or has refused), and forgetting that on shutdown would
        // make the next start send it again.
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private static async Task<bool> RecipientIsInactiveAsync(IServiceProvider services, AppDbContext db, string toEmail, CancellationToken ct)
    {
        var normalized = services.GetRequiredService<IPiiProtector>().Hash(toEmail);
        return await db.Users.AnyAsync(u => u.NormalizedEmail == normalized && !u.IsActive, ct);
    }

    private void Apply(EmailOutboxItem item, EmailSendResult result, DateTime nowUtc)
    {
        item.Attempts++;
        item.LeasedUntilUtc = null;
        item.LastError = Truncate(result.Detail);

        switch (result.Outcome)
        {
            case EmailSendOutcome.Sent:
                item.Status = EmailOutboxStatus.Sent;
                item.CompletedAtUtc = nowUtc;
                break;

            case EmailSendOutcome.Skipped:
                item.Status = EmailOutboxStatus.Skipped;
                item.CompletedAtUtc = nowUtc;
                break;

            case EmailSendOutcome.Failed:
                item.Status = EmailOutboxStatus.Failed;
                item.CompletedAtUtc = nowUtc;
                _logger.LogWarning("Outbox message {OutboxId} was refused for good: {Detail}", item.Id, result.Detail);
                break;

            case EmailSendOutcome.Retry when EmailRetryPolicy.ShouldGiveUp(item.Attempts):
                item.Status = EmailOutboxStatus.Failed;
                item.CompletedAtUtc = nowUtc;
                item.LastError = Truncate($"Gave up after {item.Attempts} attempts. Last answer: {result.Detail}");
                _logger.LogWarning("Outbox message {OutboxId} was given up after {Attempts} attempts: {Detail}", item.Id, item.Attempts, result.Detail);
                break;

            default: // Retry, attempts left
                item.NextAttemptAtUtc = nowUtc + EmailRetryPolicy.DelayAfter(item.Attempts);
                break;
        }

        // Once the message will not be sent again (delivered, deliberately skipped, or given up on) the body is no longer
        // needed. It may hold a password-reset link or a temporary password, so it is dropped the moment it becomes useless
        // rather than kept in the table for days until the clean-up removes the row.
        if (item.Status is EmailOutboxStatus.Sent or EmailOutboxStatus.Skipped or EmailOutboxStatus.Failed)
            item.HtmlBody = string.Empty;
    }

    private static string? Truncate(string? text) =>
        text is null || text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
}
