using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Entities;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-31.1: a ticket made from an e-mail was made by two requests (the ticket, then "this e-mail is now that ticket" with the e-mail's files). Now it is one:
/// the ticket, the e-mail's files and the "converted" mark are saved together, asking again for the same e-mail gives the same ticket, and a mailbox that cannot
/// be reached makes no ticket. The real application against a real database; only Microsoft is a stand-in.
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketFromEmailTests : IAsyncLifetime
{
    private const string OneFile = """{"value":[{"name":"log.txt","contentType":"text/plain","contentBytes":"aGVsbG8="}]}""";
    private const string Marker = "TFE-";

    private readonly ApiWorld _world;
    private readonly string _message = "msg-" + Guid.NewGuid().ToString("N");
    private Guid _topic, _plan;
    private bool _madeConnection;

    public TicketFromEmailTests(ApiWorld world) => _world = world;

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        using var scope = _world.Factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("EmailConnection.OAuthTokens");
        var topic = new HelpTopic { Name = "TFE " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "TFE " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db =>
        {
            db.AddRange(topic, plan);
            if (!await db.EmailConnections.AnyAsync(c => c.DepartmentId == _world.BranchA))
            {
                _madeConnection = true;
                db.EmailConnections.Add(new EmailConnection
                {
                    DepartmentId = _world.BranchA,
                    ConnectedByUserId = _world.Admin.Id,
                    ConnectedEmail = "servis@example.test",
                    AccessToken = protector.Protect("access"),
                    RefreshToken = protector.Protect("refresh"),
                    AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                });
            }
            return await db.SaveChangesAsync();
        });
        (_topic, _plan) = (topic.Id, plan.Id);
        _world.Factory.Graph.AttachmentsJson = OneFile;
        _world.Factory.Graph.Down = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        _world.Factory.Graph.Down = false;
        await _world.Db(async db =>
        {
            var tickets = await db.Tickets.Where(t => t.Title.StartsWith(Marker)).Select(t => t.Id).ToListAsync();
            await db.TicketAttachments.Where(a => tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.EmailTicketMarks.Where(m => m.MessageId == _message).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketMessages.Where(m => tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => tickets.Contains(t.Id)).ExecuteDeleteAsync();
            if (_madeConnection) await db.EmailConnections.Where(c => c.DepartmentId == _world.BranchA).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => h.Id == _topic).ExecuteDeleteAsync();
            return await db.SlaPlans.Where(p => p.Id == _plan).ExecuteDeleteAsync();
        });
    }

    private Task<ApiResponse> CreateAsync(string title, string key) =>
        _world.Send(_world.EmpA1, HttpMethod.Post, "/api/tickets/on-behalf", new
        {
            clientEmail = _world.CliX.Email,
            title,
            description = "<p>iz maila</p>",
            source = "Email",
            helpTopicId = _topic,
            departmentId = _world.BranchA,
            slaPlanId = _plan,
            priority = "Medium",
            assignedToUserId = _world.EmpA1.Id,
            emailMessageId = _message
        }, new Dictionary<string, string> { ["Idempotency-Key"] = key });

    private static Guid IdOf(ApiResponse r) => JsonDocument.Parse(r.Text).RootElement.GetProperty("id").GetGuid();

    [SqlFact]
    public async Task The_ticket_the_files_of_the_e_mail_and_the_converted_mark_are_saved_together()
    {
        _world.Factory.Commands.Clear();

        var created = await CreateAsync(Marker + "jedan", "tfe-key-1-" + _message);

        Assert.Equal(200, created.Code);
        var ticket = IdOf(created);
        Assert.Equal(1, await _world.Db(db => db.TicketAttachments.CountAsync(a => a.TicketId == ticket)));
        Assert.Equal(ticket, await _world.Db(db => db.EmailTicketMarks.Where(m => m.DepartmentId == _world.BranchA && m.MessageId == _message).Select(m => m.ConvertedTicketId).SingleAsync()));
        Assert.True(_world.Factory.Commands.AnyCommandNamingAll("Tickets", "TicketAttachments", "EmailTicketMarks"), "one save for the ticket, its files and the mark");
    }

    [SqlFact]
    public async Task Asking_again_for_the_same_e_mail_gives_the_same_ticket_without_a_second_copy_of_its_files()
    {
        var first = await CreateAsync(Marker + "dva", "tfe-key-a-" + _message);
        var again = await CreateAsync(Marker + "dva", "tfe-key-b-" + _message); // another key: a colleague, or a second click that outran the first answer

        Assert.Equal(200, again.Code);
        Assert.Equal(IdOf(first), IdOf(again));
        Assert.Equal(1, await _world.Db(db => db.Tickets.CountAsync(t => t.Title == Marker + "dva")));
        Assert.Equal(1, await _world.Db(db => db.TicketAttachments.CountAsync(a => a.TicketId == IdOf(first))));
    }

    [SqlFact]
    public async Task When_the_mailbox_cannot_be_reached_no_ticket_is_made()
    {
        _world.Factory.Graph.Down = true;

        var created = await CreateAsync(Marker + "tri", "tfe-key-3-" + _message);

        Assert.NotEqual(200, created.Code);
        Assert.False(await _world.Db(db => db.Tickets.AnyAsync(t => t.Title == Marker + "tri")));
        Assert.False(await _world.Db(db => db.EmailTicketMarks.AnyAsync(m => m.MessageId == _message)));
    }
}
