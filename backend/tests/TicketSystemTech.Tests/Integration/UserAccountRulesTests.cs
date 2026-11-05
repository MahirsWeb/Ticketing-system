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
