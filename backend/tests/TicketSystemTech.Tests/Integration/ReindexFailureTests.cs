using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// NOVO-ARC-13: the reindex stops after five failures in a row, on the reading that the provider is down or out of quota. Five
/// tickets that the provider refuses one by one (a text it will not take) look exactly the same from there, and used to end the
/// whole run with a message blaming the settings while every ticket after them was perfectly fine.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReindexFailureTests : IAsyncLifetime
{
    private const string Marker = "REJECTME";

    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();

    public ReindexFailureTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync()
    {
        if (_world.Available) { _world.Factory.Embeddings.Enabled = true; _world.Factory.Embeddings.Rejects = null; }
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        _world.Factory.Embeddings.Enabled = true;
        _world.Factory.Embeddings.Rejects = null;
        await _world.Db(async db =>
        {
            await db.KnowledgeBaseChunks.Where(c => c.TicketId != null && _tickets.Contains(c.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    /// <summary>A closed ticket with enough internal notes to be indexed. <paramref name="closedMinutesAgo"/> decides the order the reindex takes them in (newest first).</summary>
    private async Task<Guid> TicketAsync(string title, int closedMinutesAgo)
    {
        var closedAt = DateTime.UtcNow.AddMinutes(-closedMinutesAgo);
        var ticket = new Ticket
        {
            TicketNumber = "RF-" + Guid.NewGuid().ToString("N")[..8],
            Title = title,
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.Closed,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            OpenedAtUtc = closedAt.AddHours(-1),
            ClosedAtUtc = closedAt,
            ResolutionSummary = "done"
        };
        ticket.Messages.Add(new TicketMessage { AuthorId = _world.EmpA1.Id, Type = MessageType.InternalNote, BodyHtml = new string('n', 200) });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private async Task<(string State, string Message)> ReindexAsync()
    {
        var started = await _world.Post(_world.Admin, "/api/knowledge-base/reindex-all");
        Assert.Equal(202, started.Code);
        for (var i = 0; i < 300; i++)
        {
            var status = await _world.Get(_world.Admin, "/api/knowledge-base/reindex-status");
            var state = status.Json.GetProperty("state").ToString();
            if (state != "Running") return (state, status.Json.GetProperty("message").ToString());
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException("the reindex did not finish");
    }

    private Task<bool> HasVectorAsync(Guid ticket) =>
        _world.Db(db => db.KnowledgeBaseChunks.AsNoTracking().AnyAsync(c => c.TicketId == ticket && c.Embedding != null));

    [SqlFact]
    public async Task Five_tickets_the_provider_refuses_one_by_one_do_not_stop_the_tickets_after_them()
    {
        // The five newest are refused; the provider answers everything else, including the check made when the fifth one fails.
        var refused = new List<Guid>();
        for (var i = 0; i < 5; i++) refused.Add(await TicketAsync($"{Marker} {i}", closedMinutesAgo: 1000 + i));
        var fine = await TicketAsync("fine", closedMinutesAgo: 2000);
        _world.Factory.Embeddings.Rejects = text => text.Contains(Marker);

        var (state, message) = await ReindexAsync();

        Assert.Equal("Completed", state);
        Assert.Contains("could not be embedded", message);
        Assert.True(await HasVectorAsync(fine), "the ticket after the refused ones was never reached");
        foreach (var id in refused) Assert.False(await HasVectorAsync(id));
    }

    [SqlFact]
    public async Task When_the_provider_itself_is_down_the_reindex_still_stops_after_five_in_a_row()
    {
        for (var i = 0; i < 5; i++) await TicketAsync($"down {i}", closedMinutesAgo: 1000 + i);
        var after = await TicketAsync("after", closedMinutesAgo: 2000);
        _world.Factory.Embeddings.Rejects = _ => true;

        var (state, message) = await ReindexAsync();

        Assert.Equal("Failed", state);
        Assert.Contains("Stopped after 5", message);
        Assert.False(await HasVectorAsync(after), "carried on after the provider had stopped answering");
    }
}
