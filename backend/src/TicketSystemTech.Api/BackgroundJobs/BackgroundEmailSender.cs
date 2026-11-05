using System.Threading.Channels;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Api.BackgroundJobs;

/// <summary>
/// In-memory queue plus the worker that drains it. All the worker does with a message is hand it to <see cref="IEmailSender"/>,
/// which is the durable outbox: it writes a row to the database and the dispatcher delivers it (with retries) from there. So this
/// queue only takes that write off the request, which keeps a password-reset request for an address that exists as fast as one
/// for an address that does not. What a restart can lose is the few messages still waiting here, in the milliseconds before
/// their row is written (a reset link the user can simply ask for again); anything that must not be lost is filed in the
/// outbox directly, not through here.
/// </summary>
public sealed class BackgroundEmailSender : BackgroundService, IBackgroundEmailQueue
{
    private sealed record Message(string To, string Subject, string Html);

    private readonly Channel<Message> _queue = Channel.CreateBounded<Message>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundEmailSender> _logger;

    public BackgroundEmailSender(IServiceScopeFactory scopeFactory, ILogger<BackgroundEmailSender> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(string toEmail, string subject, string htmlBody)
    {
        if (!_queue.Writer.TryWrite(new Message(toEmail, subject, htmlBody)))
            _logger.LogWarning("Background email queue is full; a message was dropped");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                        .SendAsync(message.To, message.Subject, message.Html, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Sending a queued email failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
