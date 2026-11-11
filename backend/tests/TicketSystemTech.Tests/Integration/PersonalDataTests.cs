using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
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

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
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

    private async Task<Guid> TicketOfAsync(Actor client, string title, string messageHtml, string description = "<p>description</p>")
    {
        var ticket = new Ticket
        {
            TicketNumber = "PD-" + Guid.NewGuid().ToString("N")[..8],
            Title = title,
            Description = description,
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
    public async Task The_export_holds_what_the_person_wrote_in_the_ticket_itself_and_what_was_done_to_their_account()
    {
        var client = await PersonAsync("gdprFull", UserRole.Client);
        await TicketOfAsync(client, "Tiket s opisom", "<p>odgovor</p>", description: "<p>OPIS-XYZ-GDPR</p>");
        // Somebody else acts on the account (a regenerated password): that is recorded ABOUT the person, and a mail goes to their address.
        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/users/{client.Id}/regenerate-temp-password")).Code);

        var json = (await _world.Get(_world.Admin, $"/api/users/{client.Id}/export")).Json;

        Assert.Contains("OPIS-XYZ-GDPR", json.GetProperty("ticketsOpened").EnumerateArray().Select(t => t.GetProperty("description").GetString()).Single());
        Assert.Contains("RegenerateTempPassword", json.GetProperty("actionsRecordedAboutThePerson").EnumerateArray().Select(a => a.GetProperty("action").GetString()));
        Assert.NotEmpty(json.GetProperty("emailsAddressedToThePerson").EnumerateArray());
        Assert.DoesNotContain(_world.Admin.Id.ToString(), json.GetProperty("actionsRecordedAboutThePerson").GetRawText()); // who did it is the other person's data
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

    // TS-10.3: the application wrote the name of staff into the text of what it records. Making a person anonymous takes it out of
    // those sentences, and only those.
    [SqlFact]
    public async Task The_name_of_an_anonymized_person_leaves_the_sentences_the_system_wrote_and_nothing_a_person_typed()
    {
        var person = await PersonAsync("gdprName", UserRole.Employee);
        var name = await _world.Db(db => db.Users.Where(u => u.Id == person.Id).Select(u => u.FirstName + " " + u.LastName).SingleAsync());
        var ticket = new Ticket
        {
            TicketNumber = "PD-" + Guid.NewGuid().ToString("N")[..8],
            Title = "ime u tekstu",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        ticket.Assignees.Add(new TicketAssignee { UserId = _world.EmpA2.Id, IsPrimary = true });
        ticket.Messages.Add(new TicketMessage { AuthorId = person.Id, Type = MessageType.SystemEvent, BodyHtml = $"<p>Ticket reopened by {name}.</p>" });
        ticket.Messages.Add(new TicketMessage { AuthorId = person.Id, Type = MessageType.SystemEvent, BodyHtml = $"<p>Ticket closed by {name}extra.</p>" }); // someone with a longer name
        ticket.Messages.Add(new TicketMessage { AuthorId = _world.EmpA2.Id, Type = MessageType.InternalNote, BodyHtml = $"<p>Pitaj {name} za detalje.</p>" });  // typed by a person
        var notification = new Notification { UserId = _world.EmpA2.Id, Type = NotificationType.TicketAssigned, Message = $"{name} assigned you a task: \"izvještaj\"" };
        await _world.Db(async db => { db.Tickets.Add(ticket); db.Notifications.Add(notification); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);

        try
        {
            Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/users/{person.Id}/anonymize")).Code);

            var bodies = await _world.Db(db => db.TicketMessages.AsNoTracking().Where(m => m.TicketId == ticket.Id).Select(m => new { m.Type, m.BodyHtml }).ToListAsync());
            Assert.Contains(bodies, b => b.BodyHtml == "<p>Ticket reopened by Obrisan korisnik.</p>");
            Assert.Contains(bodies, b => b.BodyHtml == $"<p>Ticket closed by {name}extra.</p>");   // only whole names
            Assert.Contains(bodies, b => b.Type == MessageType.InternalNote && b.BodyHtml == $"<p>Pitaj {name} za detalje.</p>"); // nobody's own words
            Assert.Equal("Obrisan korisnik assigned you a task: \"izvještaj\"", await _world.Db(db => db.Notifications.Where(n => n.Id == notification.Id).Select(n => n.Message).SingleAsync()));
        }
        finally
        {
            await _world.Db(db => db.Notifications.Where(n => n.Id == notification.Id).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task An_Admin_cannot_anonymize_themselves_and_an_unknown_person_is_not_found()
    {
        var admin = await PersonAsync("gdprAdmin", UserRole.Admin);

        Assert.Equal(400, (await _world.Post(admin, $"/api/users/{admin.Id}/anonymize")).Code);
        Assert.Equal(404, (await _world.Post(_world.Admin, $"/api/users/{Guid.NewGuid()}/anonymize")).Code);
        Assert.Equal(404, (await _world.Get(_world.Admin, $"/api/users/{Guid.NewGuid()}/export")).Code);
    }

    [SqlFact]
    public async Task One_outbox_row_that_can_not_be_read_does_not_stop_the_export_and_is_reported()
    {
        var client = await PersonAsync("gdprBroken", UserRole.Client);
        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/users/{client.Id}/regenerate-temp-password")).Code); // so that there is a readable row for this person too

        // A row written under a key that is gone: real ciphertext that this application's protector cannot open.
        var foreign = _world.Factory.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("another purpose").Protect("someone@lost.test");
        await _world.Db(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [EmailOutbox] ([Id],[CreatedAt],[ToEmail],[Subject],[HtmlBody],[Status],[Attempts],[NextAttemptAtUtc]) VALUES ({Guid.NewGuid()}, SYSUTCDATETIME(), {foreign}, N'Unreadable', N'', 1, 1, '2000-01-01')"));
        try
        {
            var export = await _world.Get(_world.Admin, $"/api/users/{client.Id}/export");

            Assert.Equal(200, export.Code);
            Assert.True(export.Json.GetProperty("outboxRowsThatCouldNotBeRead").GetInt32() >= 1);
            Assert.NotEmpty(export.Json.GetProperty("emailsAddressedToThePerson").EnumerateArray()); // the readable ones are still there
        }
        finally
        {
            await _world.Db(db => db.Database.ExecuteSqlRawAsync("DELETE FROM [EmailOutbox] WHERE [Subject] = N'Unreadable'"));
        }
    }
}
