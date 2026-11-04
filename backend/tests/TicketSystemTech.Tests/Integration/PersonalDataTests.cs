using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// T-10 (TS-10.1): what the system holds about a person can be handed over as one file, and a person can be made anonymous
/// without losing the history of the work.
/// </summary>
[Collection(ApiCollection.Name)]
public class PersonalDataTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Actor> _people = new();
    private readonly List<Guid> _tickets = new();

    public PersonalDataTests(ApiWorld world) => _world = world;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_world.Available || _people.Count == 0) return;
        var ids = _people.Select(p => p.Id).ToList();
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();   // its messages go with it
            await db.Notifications.Where(n => ids.Contains(n.UserId)).ExecuteDeleteAsync();
            return await db.AuditLogs.Where(a => a.EntityId != null && ids.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
        });
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in ids)
            if (await users.FindByIdAsync(id.ToString()) is { } user) await users.DeleteAsync(user);
    }

    private async Task<Actor> PersonAsync(string key, UserRole role, string? phone = null)
    {
        var person = await _world.CreateActorAsync(key + Guid.NewGuid().ToString("N")[..6], role,
            role == UserRole.Employee ? _world.BranchA : null, role == UserRole.Client ? _world.CompanyId : null, phone);
        _people.Add(person);
        return person;
    }

    private async Task<Guid> TicketOfAsync(Actor client, string title, string messageHtml)
    {
        var ticket = new Ticket
        {
            TicketNumber = "PD-" + Guid.NewGuid().ToString("N")[..8],
            Title = title,
            Description = "<p>description</p>",
            ClientId = client.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.New,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        ticket.Messages.Add(new TicketMessage { AuthorId = client.Id, Type = MessageType.Response, BodyHtml = messageHtml });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    // ======================= export =======================

    [SqlFact]
    public async Task An_Admin_gets_everything_held_about_a_person_in_one_file()
    {
        var client = await PersonAsync("gdprClient", UserRole.Client, "+387 61 000 111");
        await TicketOfAsync(client, "Moj tiket za izvoz", "<p>tekst koji sam napisao</p>");
        await _world.Db(async db =>
        {
            db.Notifications.Add(new Notification { UserId = client.Id, Type = NotificationType.NewTicket, Message = "obavijest za izvoz" });
            return await db.SaveChangesAsync();
        });

        var response = await _world.Get(_world.Admin, $"/api/users/{client.Id}/export");
        var json = response.Json;

        Assert.Equal(200, response.Code);
        Assert.Contains("attachment", response.Header("Content-Disposition") ?? "");
        Assert.Equal(client.Email, json.GetProperty("profile").GetProperty("email").GetString());
        Assert.Equal("+387 61 000 111", json.GetProperty("profile").GetProperty("phoneNumber").GetString());
        Assert.Contains("Moj tiket za izvoz", json.GetProperty("ticketsOpened").EnumerateArray().Select(t => t.GetProperty("title").GetString()));
        Assert.Contains("tekst koji sam napisao", json.GetProperty("messagesWritten").EnumerateArray().Select(m => m.GetProperty("bodyHtml").GetString()).Single());
        Assert.Contains("obavijest za izvoz", json.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("message").GetString()));

        // the export itself is recorded, and the record holds no personal data
        var entry = await _world.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.Action == "UserDataExported" && a.EntityId == client.Id).Select(a => a.Details).SingleAsync());
        Assert.DoesNotContain(client.Email, entry ?? "");
    }

    [SqlFact]
    public async Task Only_an_Admin_may_export_or_anonymize()
    {
        var client = await PersonAsync("gdprTarget", UserRole.Client);

        foreach (var path in new[] { $"/api/users/{client.Id}/export" })
        {
            Assert.Equal(401, (await _world.Get(null, path)).Code);
            Assert.Equal(403, (await _world.Get(_world.EmpA1, path)).Code);
            Assert.Equal(403, (await _world.Get(client, path)).Code);
        }

        Assert.Equal(401, (await _world.Post(null, $"/api/users/{client.Id}/anonymize")).Code);
        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/users/{client.Id}/anonymize")).Code);
        Assert.Equal(403, (await _world.Post(client, $"/api/users/{client.Id}/anonymize")).Code);
    }

    // ======================= anonymize =======================

    [SqlFact]
    public async Task An_anonymized_person_is_gone_but_the_history_of_the_work_is_not()
    {
        var client = await PersonAsync("gdprGone", UserRole.Client, "+387 61 000 222");
        var ticket = await TicketOfAsync(client, "Tiket koji ostaje", "<p>poruka koja ostaje</p>");
        await _world.Db(async db =>
        {
            db.Notifications.Add(new Notification { UserId = client.Id, Type = NotificationType.NewTicket, Message = "za brisanje" });
            return await db.SaveChangesAsync();
        });

        var response = await _world.Post(_world.Admin, $"/api/users/{client.Id}/anonymize");

        Assert.Equal(200, response.Code);

        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(client.Id.ToString()))!;
        Assert.Equal("Obrisan", user.FirstName);
        Assert.Equal("korisnik", user.LastName);
        Assert.Null(user.PhoneNumber);
        Assert.False(user.IsActive);
        Assert.Null(user.PasswordHash);
        Assert.EndsWith("@anonymized.invalid", user.Email);
        Assert.DoesNotContain("gdprGone", user.Email + user.UserName, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await users.FindByEmailAsync(client.Email));                       // the old address finds nobody

        // what the work needs is still there, and still points at the same (now nameless) person
        Assert.Equal(client.Id, await _world.Db(db => db.Tickets.AsNoTracking().Where(t => t.Id == ticket).Select(t => t.ClientId).SingleAsync()));
        Assert.Equal(1, await _world.Db(db => db.TicketMessages.CountAsync(m => m.TicketId == ticket && m.AuthorId == client.Id)));
        Assert.Equal(0, await _world.Db(db => db.Notifications.CountAsync(n => n.UserId == client.Id)));

        // the old sign-in and the old session are dead
        Assert.Equal(401, (await _world.Post(null, "/api/auth/login", new { email = client.Email, password = ApiWorld.Password })).Code);
        _world.ForgetCachedStatus(client);
        Assert.Equal(401, (await _world.Get(client, "/api/tickets")).Code);

        // the audit trail says it happened, and holds none of what was removed
        var details = await _world.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.Action == "UserAnonymized" && a.EntityId == client.Id).Select(a => a.Details).SingleAsync());
        Assert.DoesNotContain(client.Email, details ?? "");
        Assert.DoesNotContain("gdprGone", details ?? "", StringComparison.OrdinalIgnoreCase);

        // and an export of the person afterwards holds the placeholder, not the original address
        var export = await _world.Get(_world.Admin, $"/api/users/{client.Id}/export");
        Assert.DoesNotContain(client.Email, export.Text);
        Assert.DoesNotContain("+387 61 000 222", export.Text);

        // doing it twice is not an error
        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/users/{client.Id}/anonymize")).Code);
    }

    [SqlFact]
    public async Task An_Admin_cannot_anonymize_themselves_and_an_unknown_person_is_not_found()
    {
        var admin = await PersonAsync("gdprAdmin", UserRole.Admin);

        Assert.Equal(400, (await _world.Post(admin, $"/api/users/{admin.Id}/anonymize")).Code);
        Assert.Equal(404, (await _world.Post(_world.Admin, $"/api/users/{Guid.NewGuid()}/anonymize")).Code);
        Assert.Equal(404, (await _world.Get(_world.Admin, $"/api/users/{Guid.NewGuid()}/export")).Code);
    }
}
