using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// NOVO-ARC-15 / NOVO-FE-03: finishing the conversion of an e-mail into a ticket can be asked twice (a retry after a dropped connection).
/// The second time it must neither copy the e-mail's files onto the ticket again nor turn the e-mail into a second ticket.
/// No mailbox is connected in these tests: a request that got as far as the mailbox would fail with "connect your mailbox", so a 200 here
/// can only come from the request being recognised as already done.
/// </summary>
[Collection(ApiCollection.Name)]
public class EmailConversionTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<string> _messages = new();

    public EmailConversionTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.EmailTicketMarks.Where(m => _messages.Contains(m.MessageId)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> TicketAsync()
    {
        var ticket = new Ticket
        {
            TicketNumber = "EC-" + Guid.NewGuid().ToString("N")[..8],
            Title = "iz maila",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.New,
            DepartmentId = _world.BranchA,
            Source = TicketSource.Email
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private async Task<string> ConvertedMessageAsync(Guid ticket)
    {
        var message = "msg-" + Guid.NewGuid().ToString("N");
        await _world.Db(async db =>
        {
            db.EmailTicketMarks.Add(new EmailTicketMark { DepartmentId = _world.BranchA, MessageId = message, IsMarked = true, MarkedByUserId = _world.EmpA1.Id, ConvertedTicketId = ticket });
            return await db.SaveChangesAsync();
        });
        _messages.Add(message);
        return message;
    }

    [SqlFact]
    public async Task Finishing_the_conversion_again_for_the_same_ticket_is_a_no_op()
    {
        var ticket = await TicketAsync();
        var message = await ConvertedMessageAsync(ticket);

        var again = await _world.Post(_world.EmpA1, $"/api/email-integration/messages/{message}/complete-ticket?ticketId={ticket}");

        Assert.Equal(200, again.Code);
    }

    [SqlFact]
    public async Task An_e_mail_that_already_became_a_ticket_cannot_become_another_one()
    {
        var first = await TicketAsync();
        var second = await TicketAsync();
        var message = await ConvertedMessageAsync(first);

        var other = await _world.Post(_world.EmpA1, $"/api/email-integration/messages/{message}/complete-ticket?ticketId={second}");

        Assert.Equal(409, other.Code);
        Assert.Contains("already became another ticket", other.Text); // not the "connect your mailbox" 409 that the same request would get later on
        Assert.Equal(first, await _world.Db(db => db.EmailTicketMarks.Where(m => m.MessageId == message).Select(m => m.ConvertedTicketId).SingleAsync()));
    }
}
