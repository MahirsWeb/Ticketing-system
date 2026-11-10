using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>Rules about who a task may be handed to, and who may run its timer, that nothing checked until the task code was moved out of its controller (T-15).</summary>
[Collection(ApiCollection.Name)]
public class WorkTaskRulesTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tasks = new();
    private readonly List<Actor> _people = new();

    public WorkTaskRulesTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        var personIds = _people.Select(p => p.Id).ToList();
        await _world.Db(async db =>
        {
            await db.WorkTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => personIds.Contains(n.UserId)).ExecuteDeleteAsync();
            return await db.Notifications.Where(n => n.Message.Contains("RULES-TASK")).ExecuteDeleteAsync();
        });
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var person in _people)
            if (await users.FindByIdAsync(person.Id.ToString()) is { } user) await users.DeleteAsync(user);
    }

    private async Task<ApiResponse> CreateTaskAsync(Actor by, Actor to)
    {
        var response = await _world.Post(by, "/api/work-tasks", new { title = "RULES-TASK " + Guid.NewGuid().ToString("N")[..6], description = "d", assignedToUserId = to.Id });
        if (response.Code == 200) _tasks.Add(response.Json.GetProperty("id").GetGuid());
        return response;
    }

    private async Task<Actor> BranchAEmployeeAsync()
    {
        var person = await _world.CreateActorAsync("tasktmp" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, _world.BranchA, null);
        _people.Add(person);
        return person;
    }

    [SqlFact]
    public async Task A_task_cannot_be_assigned_to_or_handed_to_a_deactivated_colleague()
    {
        var worker = await BranchAEmployeeAsync();
        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{worker.Id}/active", new { isActive = false })).Code);

        var created = await CreateTaskAsync(_world.EmpA1, worker);
        Assert.Equal(400, created.Code);
        Assert.Contains("inactive", created.Text);

        // and an existing task cannot be re-assigned onto a deactivated colleague either
        var live = await CreateTaskAsync(_world.EmpA1, _world.EmpA2);
        var reassigned = await _world.Post(_world.EmpA1, $"/api/work-tasks/{live.Json.GetProperty("id").GetGuid()}/reassign", new { assignedToUserId = worker.Id });
        Assert.Equal(400, reassigned.Code);
    }

    [SqlFact]
    public async Task Deactivating_an_employee_stops_their_running_task_timer()
    {
        var worker = await BranchAEmployeeAsync();
        var created = await CreateTaskAsync(_world.EmpA1, worker);
        var id = created.Json.GetProperty("id").GetGuid();
        Assert.Equal(200, (await _world.Post(worker, $"/api/work-tasks/{id}/start")).Code);

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{worker.Id}/active", new { isActive = false })).Code);

        // the running timer is closed, not left counting to "now" for ever
        var stillRunning = await _world.Db(db => db.WorkTaskSessions.AsNoTracking().AnyAsync(s => s.WorkTaskId == id && s.EndedAtUtc == null));
        Assert.False(stillRunning);
    }

    [SqlFact]
    public async Task A_task_goes_to_a_colleague_of_the_same_branch_and_only_an_admin_may_cross_branches()
    {
        Assert.Equal(200, (await CreateTaskAsync(_world.EmpA1, _world.EmpA2)).Code);

        var acrossBranches = await CreateTaskAsync(_world.EmpA1, _world.EmpB1);
        Assert.Equal(400, acrossBranches.Code);
        Assert.Contains("own branch", acrossBranches.Text);

        Assert.Equal(200, (await CreateTaskAsync(_world.Admin, _world.EmpB1)).Code);

        var toAClient = await CreateTaskAsync(_world.EmpA1, _world.CliX);
        Assert.Equal(400, toAClient.Code);
    }

    [SqlFact]
    public async Task A_task_cannot_go_to_somebody_who_belongs_to_no_branch()
    {
        var drifter = await _world.CreateActorAsync("taskdrifter" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, null, null);
        _people.Add(drifter);

        var response = await CreateTaskAsync(_world.Admin, drifter);

        Assert.Equal(400, response.Code);
        Assert.Contains("not linked to a branch", response.Text);
    }

    [SqlFact]
    public async Task A_task_is_handed_on_only_inside_its_branch()
    {
        var created = await CreateTaskAsync(_world.EmpA1, _world.EmpA2);
        var id = created.Json.GetProperty("id").GetGuid();

        var elsewhere = await _world.Post(_world.EmpA1, $"/api/work-tasks/{id}/reassign", new { assignedToUserId = _world.EmpB1.Id });
        Assert.Equal(400, elsewhere.Code);
        Assert.Contains("same branch", elsewhere.Text);

        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{id}/reassign", new { assignedToUserId = _world.EmpA1.Id })).Code);
    }

    [SqlFact]
    public async Task Only_the_person_a_task_is_assigned_to_runs_its_timer_and_only_one_at_a_time()
    {
        var created = await CreateTaskAsync(_world.EmpA1, _world.EmpA2);
        var id = created.Json.GetProperty("id").GetGuid();

        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{id}/start")).Code);
        Assert.Equal(200, (await _world.Post(_world.EmpA2, $"/api/work-tasks/{id}/start")).Code);

        var again = await _world.Post(_world.EmpA2, $"/api/work-tasks/{id}/start");
        Assert.Equal(400, again.Code);
        Assert.Contains("already running", again.Text);

        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{id}/stop")).Code);
        Assert.Equal(200, (await _world.Post(_world.EmpA2, $"/api/work-tasks/{id}/stop")).Code);

        var notRunning = await _world.Post(_world.EmpA2, $"/api/work-tasks/{id}/stop");
        Assert.Equal(400, notRunning.Code);
        Assert.Contains("not currently running", notRunning.Text);
    }
}
