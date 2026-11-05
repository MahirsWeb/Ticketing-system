using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Rules of notifications, attachments and the shared mailbox that nothing checked until their code was moved out of the controllers (T-15).
/// Each test makes the people and rows it needs and removes them.
/// </summary>
[Collection(ApiCollection.Name)]
public class SmallControllerRulesTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Actor> _people = new();
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _branches = new();
    private readonly List<Guid> _connections = new();

    public SmallControllerRulesTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        var ids = _people.Select(p => p.Id).ToList();
        await _world.Db(async db =>
        {
            await db.TicketAttachments.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => ids.Contains(n.UserId)).ExecuteDeleteAsync();
            await db.EmailConnections.Where(c => _connections.Contains(c.Id)).ExecuteDeleteAsync();
            return await db.AuditLogs.Where(a => a.EntityId != null && (ids.Contains(a.EntityId.Value) || _branches.Contains(a.EntityId.Value))).ExecuteDeleteAsync();
        });
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in ids)
            if (await users.FindByIdAsync(id.ToString()) is { } user) await users.DeleteAsync(user);
        await _world.Db(db => db.Departments.Where(d => _branches.Contains(d.Id)).ExecuteDeleteAsync());
    }

    private async Task<Actor> PersonAsync(string key, UserRole role, Guid? branch = null)
    {
        var person = await _world.CreateActorAsync(key + Guid.NewGuid().ToString("N")[..6], role, branch, role == UserRole.Client ? _world.CompanyId : null);
        _people.Add(person);
        return person;
    }

    private async Task AddNotificationsAsync(Actor person, int count)
    {
        await _world.Db(async db =>
        {
            for (var i = 0; i < count; i++)
                db.Notifications.Add(new Notification { UserId = person.Id, Type = NotificationType.NewResponse, Message = $"note {i}" });
            return await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> TicketAsync(Actor client, Guid branch)
    {
        var ticket = new Ticket
        {
            TicketNumber = "SC-" + Guid.NewGuid().ToString("N")[..8],
            Title = "small controllers",
            Description = "<p>x</p>",
            ClientId = client.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.New,
            DepartmentId = branch,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    // ======================= notifications =======================

    [SqlFact]
    public async Task Reading_all_notifications_marks_only_the_callers_own()
    {
        var first = await PersonAsync("noteone", UserRole.Client);
        var second = await PersonAsync("notetwo", UserRole.Client);
        await AddNotificationsAsync(first, 2);
        await AddNotificationsAsync(second, 2);

        Assert.Equal(200, (await _world.Post(first, "/api/notifications/read-all")).Code);

        Assert.Equal(2, await _world.Db(db => db.Notifications.CountAsync(n => n.UserId == first.Id && n.IsRead)));
        Assert.Equal(0, await _world.Db(db => db.Notifications.CountAsync(n => n.UserId == second.Id && n.IsRead)));
    }

    [SqlFact]
    public async Task The_list_is_the_callers_own_newest_first_and_never_longer_than_a_hundred()
    {
        var person = await PersonAsync("notelist", UserRole.Client);
        await AddNotificationsAsync(person, 105);

        Assert.Equal(100, (await _world.Get(person, "/api/notifications?take=100000")).Json.GetArrayLength());
        Assert.Equal(1, (await _world.Get(person, "/api/notifications?take=0")).Json.GetArrayLength()); // at least one, whatever is asked
        Assert.Equal(20, (await _world.Get(person, "/api/notifications")).Json.GetArrayLength());     // the default
    }

    // ======================= attachments =======================

    private static MultipartFormDataContent File(string name, int bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[bytes]), "files", name);
        return form;
    }

    [SqlFact]
    public async Task An_attachment_must_be_an_allowed_type_and_no_bigger_than_10_MB()
    {
        var client = await PersonAsync("attachowner", UserRole.Client);
        var ticket = await TicketAsync(client, _world.BranchA);

        var wrongType = await _world.Post(client, $"/api/tickets/{ticket}/attachments", File("virus.exe", 100));
        Assert.Equal(400, wrongType.Code);
        Assert.Contains("not allowed", wrongType.Text);

        var tooBig = await _world.Post(client, $"/api/tickets/{ticket}/attachments", File("velika.pdf", 11 * 1024 * 1024));
        Assert.Equal(400, tooBig.Code);
        Assert.Contains("10 MB", tooBig.Text);

        var fine = await _world.Post(client, $"/api/tickets/{ticket}/attachments", File("mala.pdf", 100));
        Assert.Equal(200, fine.Code);
        Assert.Equal(1, await _world.Db(db => db.TicketAttachments.CountAsync(a => a.TicketId == ticket)));
    }

    // ======================= the shared mailbox =======================

    [SqlFact]
    public async Task A_member_of_a_branch_cannot_mark_an_e_mail_as_the_ticket_of_another_branch()
    {
        var client = await PersonAsync("mailowner", UserRole.Client);
        var ofB = await TicketAsync(client, _world.BranchB);

        var elsewhere = await _world.Post(_world.EmpA1, $"/api/email-integration/messages/some-message/complete-ticket?ticketId={ofB}");
        Assert.Equal(403, elsewhere.Code);

        var missing = await _world.Post(_world.EmpA1, $"/api/email-integration/messages/some-message/complete-ticket?ticketId={Guid.NewGuid()}");
        Assert.Equal(404, missing.Code);
    }

    [SqlFact]
    public async Task Staff_without_a_branch_are_told_so_and_a_connected_mailbox_is_not_replaced_by_connecting_again()
    {
        var drifter = await PersonAsync("maildrifter", UserRole.Employee);
        var noBranch = await _world.Get(drifter, "/api/email-integration/status");
        Assert.Equal(400, noBranch.Code);
        Assert.Contains("not assigned to a branch", noBranch.Text);

        // a branch of its own, so the mailbox of the shared branches stays as other tests left it
        var name = "Mailbox test " + Guid.NewGuid().ToString("N")[..6];
        var created = await _world.Post(_world.Admin, "/api/departments", new { name, email = name.Replace(' ', '-').ToLowerInvariant() + "@authz.test" });
        var branch = created.Json.GetProperty("id").GetGuid();
        _branches.Add(branch);
        var member = await PersonAsync("mailmember", UserRole.Employee, branch);
        var connection = new EmailConnection { DepartmentId = branch, ConnectedByUserId = _world.Admin.Id, ConnectedEmail = "servis@authz.test", AccessToken = "a", RefreshToken = "r", AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1), UpdatedAt = DateTime.UtcNow };
        await _world.Db(async db => { db.EmailConnections.Add(connection); return await db.SaveChangesAsync(); });
        _connections.Add(connection.Id);

        var again = await _world.Post(member, "/api/email-integration/connect", new { code = "x", redirectUri = "https://localhost/callback", codeVerifier = new string('a', 64) });

        Assert.Equal(409, again.Code);
        Assert.Equal("MAILBOX_ALREADY_CONNECTED", again.Json.GetProperty("code").GetString());
    }
}
