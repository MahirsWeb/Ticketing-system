using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>Rules about who may do what to an account that nothing checked until the account code was moved out of its controller (T-15).</summary>
[Collection(ApiCollection.Name)]
public class UserAccountRulesTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Actor> _people = new();
    private readonly List<Guid> _tickets = new();

    public UserAccountRulesTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available || _people.Count == 0) return;
        var ids = _people.Select(p => p.Id).ToList();
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => ids.Contains(n.UserId)).ExecuteDeleteAsync();
            return await db.AuditLogs.Where(a => a.EntityId != null && ids.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
        });
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in ids)
            if (await users.FindByIdAsync(id.ToString()) is { } user) await users.DeleteAsync(user);
    }

    private async Task<Actor> PersonAsync(string key, UserRole role, Guid? branch = null)
    {
        var person = await _world.CreateActorAsync(key + Guid.NewGuid().ToString("N")[..6], role, branch,
            role == UserRole.Client ? _world.CompanyId : null);
        _people.Add(person);
        return person;
    }

    private async Task GiveTicketAsync(Actor client)
    {
        var ticket = new Ticket
        {
            TicketNumber = "UA-" + Guid.NewGuid().ToString("N")[..8],
            Title = "account rules",
            Description = "<p>x</p>",
            ClientId = client.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.New,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
    }

    [SqlFact]
    public async Task Only_an_admin_gets_to_see_a_temporary_password_a_colleague_resetting_it_for_a_client_does_not()
    {
        var client = await PersonAsync("uacli", UserRole.Client);
        await GiveTicketAsync(client);

        var byEmployee = await _world.Post(_world.EmpA1, $"/api/users/{client.Id}/regenerate-temp-password");
        var byAdmin = await _world.Post(_world.Admin, $"/api/users/{client.Id}/regenerate-temp-password");

        Assert.Equal(200, byEmployee.Code);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, byEmployee.Json.GetProperty("temporaryPassword").ValueKind); // it only goes to the owner by e-mail
        Assert.Equal(200, byAdmin.Code);
        Assert.False(string.IsNullOrEmpty(byAdmin.Json.GetProperty("temporaryPassword").GetString()));
    }

    [SqlFact]
    public async Task A_new_temporary_password_is_mailed_under_its_own_subject_with_the_password_in_it()
    {
        var client = await PersonAsync("uamail", UserRole.Client);
        await GiveTicketAsync(client);
        try
        {
            var made = await _world.Post(_world.Admin, $"/api/users/{client.Id}/regenerate-temp-password");

            Assert.Equal(200, made.Code);
            var mail = Assert.Single(await MailsTo(client.Email));
            Assert.Equal("Your new temporary password — Ticket System Tech", mail.Subject);
            Assert.Contains(made.Json.GetProperty("temporaryPassword").GetString()!, mail.HtmlBody);
        }
        finally
        {
            await ForgetMailsTo(client.Email);
        }
    }

    [SqlFact]
    public async Task A_new_account_is_mailed_the_invitation_with_its_temporary_password()
    {
        var email = $"invited-{Guid.NewGuid():N}@authz.test";
        var created = await _world.Post(_world.Admin, "/api/users/clients", new { firstName = "Invited", lastName = "Person", email, companyId = _world.CompanyId });
        Assert.Equal(200, created.Code);
        var id = created.Json.GetProperty("userId").GetGuid();
        _people.Add(new Actor("invited", id, email, UserRole.Client, null, ""));
        try
        {
            var mail = Assert.Single(await MailsTo(email));
            Assert.Equal("You've been invited to Ticket System Tech", mail.Subject);
            Assert.Contains(created.Json.GetProperty("temporaryPassword").GetString()!, mail.HtmlBody);
        }
        finally
        {
            await ForgetMailsTo(email);
        }
    }

    [SqlFact]
    public async Task An_employee_who_creates_a_client_does_not_get_the_temporary_password_back_it_is_mailed_instead()
    {
        var email = $"empmade-{Guid.NewGuid():N}@authz.test";
        var created = await _world.Post(_world.EmpA1, "/api/users/clients", new { firstName = "Phoned", lastName = "In", email, companyId = _world.CompanyId });
        Assert.Equal(200, created.Code);
        var id = created.Json.GetProperty("userId").GetGuid();
        _people.Add(new Actor("empmade", id, email, UserRole.Client, null, ""));
        try
        {
            // The employee must not learn the client's first password (they could otherwise sign in as the client before the
            // client sets their own). It is hidden from them and travels to the client's mailbox, exactly as regenerating one does.
            Assert.Equal(System.Text.Json.JsonValueKind.Null, created.Json.GetProperty("temporaryPassword").ValueKind);
            var mail = Assert.Single(await MailsTo(email));
            Assert.Equal("You've been invited to Ticket System Tech", mail.Subject);
        }
        finally
        {
            await ForgetMailsTo(email);
        }
    }

    // The address is stored encrypted, so it cannot be compared in the query: the latest messages are read and picked from here.
    private async Task<List<EmailOutboxItem>> MailsTo(string email) =>
        (await _world.Db(db => db.EmailOutbox.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(500).ToListAsync())).Where(m => m.ToEmail == email).ToList();

    private async Task ForgetMailsTo(string email)
    {
        var ids = (await MailsTo(email)).Select(m => m.Id).ToList();
        await _world.Db(db => db.EmailOutbox.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync());
    }

    [SqlFact]
    public async Task A_colleague_can_reset_the_password_of_a_client_of_the_branch_but_not_of_staff_or_of_a_client_who_never_wrote_to_it()
    {
        var colleague = await PersonAsync("uastaff", UserRole.Employee, _world.BranchA);
        var stranger = await PersonAsync("uastranger", UserRole.Client); // no ticket in branch A

        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/users/{colleague.Id}/regenerate-temp-password")).Code);
        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/users/{stranger.Id}/regenerate-temp-password")).Code);
        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/users/{colleague.Id}/regenerate-temp-password")).Code);
    }

    [SqlFact]
    public async Task An_admin_can_neither_change_their_own_type_nor_switch_themselves_off()
    {
        var admin = await PersonAsync("uaadmin", UserRole.Admin);

        var changeRole = await _world.Send(admin, HttpMethod.Put, $"/api/users/{admin.Id}", new { firstName = "A", lastName = "B", role = "Employee", departmentId = _world.BranchA });
        Assert.Equal(400, changeRole.Code);
        Assert.Contains("own account type", changeRole.Text);

        var deactivate = await _world.Send(admin, HttpMethod.Patch, $"/api/users/{admin.Id}/active", new { isActive = false });
        Assert.Equal(400, deactivate.Code);
        Assert.Contains("own account", deactivate.Text);

        Assert.Equal(UserRole.Admin, await _world.Db(db => db.Users.Where(u => u.Id == admin.Id).Select(u => u.Role).SingleAsync()));
        Assert.True(await _world.Db(db => db.Users.Where(u => u.Id == admin.Id).Select(u => u.IsActive).SingleAsync()));
    }
}
