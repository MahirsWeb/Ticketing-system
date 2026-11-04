using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// A branch that still has people or tickets cannot be deleted (TS-05.5, NOVO-DOM-05). The application says why in words, and the
/// database refuses on its own for every other way in: a ticket without a branch is visible to every employee, and a staff member
/// without one is lost to every list.
/// </summary>
[Collection(ApiCollection.Name)]
public class BranchDeletionTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _branches = new();
    private readonly List<Actor> _people = new();
    private readonly List<Guid> _tickets = new();

    public BranchDeletionTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            return await db.AuditLogs.Where(a => a.EntityId != null && _branches.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
        });
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var person in _people)
            if (await users.FindByIdAsync(person.Id.ToString()) is { } user) await users.DeleteAsync(user);
        await _world.Db(db => db.Departments.Where(d => _branches.Contains(d.Id)).ExecuteDeleteAsync());
    }

    private async Task<Guid> NewBranchAsync()
    {
        var name = "Delete test " + Guid.NewGuid().ToString("N")[..6];
        var response = await _world.Send(_world.Admin, HttpMethod.Post, "/api/departments", new { name, email = name.Replace(' ', '-').ToLowerInvariant() + "@authz.test" });
        Assert.Equal(200, response.Code);
        var id = response.Json.GetProperty("id").GetGuid();
        _branches.Add(id);
        return id;
    }

    [SqlFact]
    public async Task A_branch_with_staff_cannot_be_deleted_by_the_application_or_by_the_database()
    {
        var branch = await NewBranchAsync();
        var person = await _world.CreateActorAsync("branchStaff" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, branch, null);
        _people.Add(person);

        var response = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/departments/{branch}");

        Assert.Equal(400, response.Code);
        Assert.Contains("staff member", response.Text);
        Assert.Equal(branch, await _world.Db(db => db.Users.AsNoTracking().Where(u => u.Id == person.Id).Select(u => u.DepartmentId).SingleAsync()));

        var error = await Assert.ThrowsAsync<SqlException>(() => _world.Db(db => db.Departments.Where(d => d.Id == branch).ExecuteDeleteAsync()));
        Assert.Contains("FK_AspNetUsers_Departments_DepartmentId", error.Message);
    }

    [SqlFact]
    public async Task A_branch_that_owns_a_ticket_cannot_be_deleted_by_the_application_or_by_the_database()
    {
        var branch = await NewBranchAsync();
        var ticket = new Ticket
        {
            TicketNumber = "BD-" + Guid.NewGuid().ToString("N")[..8],
            Title = "branch deletion",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.New,
            DepartmentId = branch,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);

        var response = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/departments/{branch}");

        Assert.Equal(400, response.Code);
        Assert.Contains("ticket(s)", response.Text);
        Assert.Equal(branch, await _world.Db(db => db.Tickets.AsNoTracking().Where(t => t.Id == ticket.Id).Select(t => t.DepartmentId).SingleAsync()));

        var error = await Assert.ThrowsAsync<SqlException>(() => _world.Db(db => db.Departments.Where(d => d.Id == branch).ExecuteDeleteAsync()));
        Assert.Contains("FK_Tickets_Departments_DepartmentId", error.Message);
    }

    [SqlFact]
    public async Task An_empty_branch_can_still_be_deleted()
    {
        var branch = await NewBranchAsync();

        Assert.Equal(204, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/departments/{branch}")).Code);
    }
}
