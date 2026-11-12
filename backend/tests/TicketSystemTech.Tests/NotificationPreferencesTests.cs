using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Tests.Integration;

namespace TicketSystemTech.Tests;

/// <summary>
/// TS-47.1: the channels were chosen inside the controllers, the same for everybody. A person can now switch off the e-mail about a kind of event (the line in
/// their list and the push stay, and the e-mails about the account itself are never optional). The choice is kept on the account.
/// </summary>
public class NotificationPreferencesTests
{
    private static ApplicationUser Person(string muted = "", UserRole role = UserRole.Employee) =>
        new() { FirstName = "Ana", LastName = "Anić", Email = "ana@x.test", Role = role, MutedEmailTypes = muted };

    [Fact]
    public void A_muted_kind_gets_no_e_mail_but_still_gets_its_line_in_the_list()
    {
        var ticket = new Ticket { TicketNumber = "100001", Title = "Printer" };
        var recipients = new[] { Person("NewResponse"), Person(), Person("TicketAssigned") };
        var reply = new NewResponseEvent(ticket, recipients);
        var notifier = new Notifier(null!);

        var mails = notifier.Emails(reply);
        var lines = notifier.InApp(reply);

        Assert.Equal(2, mails.Count); // the first person switched this kind off
        Assert.Equal(3, lines.Count); // the list is not a channel anybody can switch off
    }

    [Fact]
    public void Muting_one_kind_leaves_the_others()
    {
        var ticket = new Ticket { TicketNumber = "100001", Title = "Printer" };
        var closed = new TicketClosedEvent(ticket, Person("NewResponse", UserRole.Client), Person());
        var opened = new TicketOpenedEvent(ticket, Person("NewResponse", UserRole.Client), Person());

        Assert.Single(new Notifier(null!).Emails(closed));
        Assert.Equal(2, new Notifier(null!).Emails(opened).Count); // the assignee's and the client's
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("NewResponse", 1)]
    [InlineData(" NewResponse , TicketClosed ", 2)]
    [InlineData("NewResponse,Nonsense,999", 1)]
    [InlineData(null, 0)]
    public void The_stored_choice_is_read_tolerantly(string? stored, int expected) =>
        Assert.Equal(expected, NotificationPreferences.Parse(stored).Count);

    [Fact]
    public void A_role_is_only_offered_the_e_mails_it_is_ever_sent()
    {
        Assert.Contains(NotificationType.NewTicket, NotificationPreferences.EmailTypesFor(UserRole.Employee));
        Assert.DoesNotContain(NotificationType.NewTicket, NotificationPreferences.EmailTypesFor(UserRole.Client));
        Assert.Contains(NotificationType.TicketClosed, NotificationPreferences.EmailTypesFor(UserRole.Client));
        Assert.DoesNotContain(NotificationType.TicketClosed, NotificationPreferences.EmailTypesFor(UserRole.Employee));
    }

    [Fact]
    public void Saving_writes_the_names_in_a_stable_order_without_repeats() =>
        Assert.Equal("NewTicket,NewResponse", NotificationPreferences.Serialize(new[] { NotificationType.NewResponse, NotificationType.NewTicket, NotificationType.NewResponse }));
}

[Collection(ApiCollection.Name)]
public class NotificationPreferencesApiTests
{
    private readonly ApiWorld _world;

    public NotificationPreferencesApiTests(ApiWorld world) => _world = world;

    private Task<string> StoredAsync(Guid id) => _world.Db(db => db.Users.Where(u => u.Id == id).Select(u => u.MutedEmailTypes).SingleAsync());

    private Task ResetAsync(params Guid[] ids) =>
        _world.Db(db => db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.MutedEmailTypes, "")));

    private static List<(string Type, bool Email)> Read(ApiResponse r) =>
        System.Text.Json.JsonDocument.Parse(r.Text).RootElement.EnumerateArray().Select(e => (e.GetProperty("type").GetString()!, e.GetProperty("email").GetBoolean())).ToList();

    [SqlFact]
    public async Task A_person_sees_the_e_mails_of_their_role_all_on_and_can_switch_one_off_and_on_again()
    {
        try
        {
            var before = Read(await _world.Send(_world.EmpA1, HttpMethod.Get, "/api/users/me/notification-preferences"));
            Assert.All(before, p => Assert.True(p.Email));
            Assert.Contains(before, p => p.Type == "NewTicket");
            Assert.DoesNotContain(before, p => p.Type == "TicketClosed"); // a client's e-mail, not an employee's

            var off = await _world.Send(_world.EmpA1, HttpMethod.Put, "/api/users/me/notification-preferences", new { preferences = new[] { new { type = "NewResponse", email = false } } });
            Assert.Equal(200, off.Code);
            Assert.Contains(Read(off), p => p.Type == "NewResponse" && !p.Email);
            Assert.Equal("NewResponse", await StoredAsync(_world.EmpA1.Id));

            var on = await _world.Send(_world.EmpA1, HttpMethod.Put, "/api/users/me/notification-preferences", new { preferences = new[] { new { type = "NewResponse", email = true } } });
            Assert.Equal(200, on.Code);
            Assert.Equal("", await StoredAsync(_world.EmpA1.Id));
        }
        finally
        {
            await ResetAsync(_world.EmpA1.Id);
        }
    }

    [SqlFact]
    public async Task A_kind_the_role_is_never_sent_is_refused_and_nothing_is_stored()
    {
        var refused = await _world.Send(_world.EmpA1, HttpMethod.Put, "/api/users/me/notification-preferences", new { preferences = new[] { new { type = "TicketClosed", email = false } } });
        var nonsense = await _world.Send(_world.EmpA1, HttpMethod.Put, "/api/users/me/notification-preferences", new { preferences = new[] { new { type = "Nonsense", email = false } } });
        var nobody = await _world.Send(null, HttpMethod.Get, "/api/users/me/notification-preferences");

        Assert.Equal(400, refused.Code);
        Assert.Equal(400, nonsense.Code);
        Assert.Equal(401, nobody.Code);
        Assert.Equal("", await StoredAsync(_world.EmpA1.Id));
    }

    [SqlFact]
    public async Task A_client_who_switched_off_the_reply_mail_gets_the_line_in_their_list_but_no_mail()
    {
        var ticket = new Ticket
        {
            TicketNumber = "NP-" + Guid.NewGuid().ToString("N")[..8],
            Title = "štampač",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        try
        {
            Assert.Equal(200, (await _world.Send(_world.CliX, HttpMethod.Put, "/api/users/me/notification-preferences", new { preferences = new[] { new { type = "NewResponse", email = false } } })).Code);

            var reply = await _world.Send(_world.EmpA1, HttpMethod.Post, $"/api/tickets/{ticket.Id}/messages", new { type = "Response", bodyHtml = "<p>pogledajte</p>" });
            Assert.Equal(200, reply.Code);

            Assert.True(await _world.Db(db => db.Notifications.AnyAsync(n => n.UserId == _world.CliX.Id && n.TicketId == ticket.Id && n.Type == NotificationType.NewResponse)));
            Assert.False(await _world.Db(db => db.EmailOutbox.AnyAsync(m => m.Subject.Contains(ticket.TicketNumber))));
        }
        finally
        {
            await _world.Db(async db =>
            {
                await db.Users.Where(u => u.Id == _world.CliX.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.MutedEmailTypes, ""));
                await db.EmailOutbox.Where(m => m.Subject.Contains(ticket.TicketNumber)).ExecuteDeleteAsync();
                await db.Notifications.Where(n => n.TicketId == ticket.Id).ExecuteDeleteAsync();
                await db.TicketMessages.Where(m => m.TicketId == ticket.Id).ExecuteDeleteAsync();
                return await db.Tickets.Where(t => t.Id == ticket.Id).ExecuteDeleteAsync();
            });
        }
    }
}
