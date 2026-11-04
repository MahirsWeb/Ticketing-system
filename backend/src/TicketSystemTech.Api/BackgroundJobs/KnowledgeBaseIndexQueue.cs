using System.Threading.Channels;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Api.BackgroundJobs;

/// <summary>
/// In-memory queue plus the worker that drains it (see <see cref="IKnowledgeBaseIndexQueue"/>). The queue is not durable: a
/// restart forgets what was waiting. That is acceptable here, because nothing is lost for good: the admin's reindex
/// (<c>POST /api/knowledge-base/reindex-all</c>) embeds every eligible ticket that has no vector yet.
/// </summary>
public sealed class KnowledgeBaseIndexQueue : BackgroundService, IKnowledgeBaseIndexQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<Guid> _waiting = new();
    private readonly object _gate = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KnowledgeBaseIndexQueue> _logger;

    public KnowledgeBaseIndexQueue(IServiceScopeFactory scopes, ILogger<KnowledgeBaseIndexQueue> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Enqueue(Guid ticketId)
    {
        lock (_gate)
        {
            if (!_waiting.Add(ticketId)) return; // already waiting: it will be indexed with the newest text anyway
        }
        _queue.Writer.TryWrite(ticketId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var ticketId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Forgotten before indexing starts, so a note added while it runs queues the ticket again.
                lock (_gate) _waiting.Remove(ticketId);
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IKnowledgeBaseIndexer>().IndexTicketAsync(ticketId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Indexing ticket {TicketId} for the knowledge base failed", ticketId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
