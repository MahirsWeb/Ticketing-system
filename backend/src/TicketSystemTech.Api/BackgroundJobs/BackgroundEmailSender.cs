using System.Threading.Channels;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Api.BackgroundJobs;

/// <summary>
/// In-memory queue plus the worker that drains it. The queue is not durable: a restart loses what was waiting. That is
/// acceptable for what uses it (a password-reset link the user can simply ask for again); anything that must not be
/// lost belongs in an outbox, not here.
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
