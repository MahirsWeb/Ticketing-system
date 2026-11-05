using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-34.2: who a ticket is assigned to is recorded in one place, its list of assignees, with the main one marked. There is no second
/// column that can disagree, and every way of changing the list keeps "exactly one main assignee, or nobody".
/// </summary>
[Collection(ApiCollection.Name)]
public class AssigneeListTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _lookups = new();

    public AssigneeListTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && _tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
            return await db.SlaPlans.Where(p => _lookups.Contains(p.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> TicketAsync(Actor? main, params Actor[] help)
    {
        var ticket = new Ticket
        {
            TicketNumber = "AL-" + Guid.NewGuid().ToString("N")[..8],
            Title = "assignees",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            OpenedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        // The help has been on the ticket longer than the main assignee, so nothing here can work by taking "the first one".
        if (main is not null) ticket.Assignees.Add(new TicketAssignee { UserId = main.Id, IsPrimary = true, AssignedAtUtc = DateTime.UtcNow.AddHours(-1) });
        foreach (var person in help) ticket.Assignees.Add(new TicketAssignee { UserId = person.Id, AssignedAtUtc = DateTime.UtcNow.AddHours(-2) });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private Task<List<(Guid User, bool Primary)>> ListAsync(Guid ticket) =>
        _world.Db(async db => (await db.TicketAssignees.AsNoTracking().Where(a => a.TicketId == ticket).OrderBy(a => a.AssignedAtUtc).ToListAsync())
            .Select(a => (a.UserId, a.IsPrimary)).ToList());

    private async Task<Guid?> AssignedToInDetailAsync(Guid ticket)
    {
        var detail = await _world.Get(_world.Admin, $"/api/tickets/{ticket}");
        Assert.Equal(200, detail.Code);
        var value = detail.Json.GetProperty("assignedToUserId");
        return value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : value.GetGuid();
    }

    [SqlFact]
    public async Task The_main_assignee_is_the_marked_one_on_the_list_and_everybody_else_is_help()
    {
        var id = await TicketAsync(_world.EmpA1, _world.EmpA2);

        Assert.Equal(_world.EmpA1.Id, await AssignedToInDetailAsync(id));
        Assert.Single(await ListAsync(id), x => x.Primary);

        // "My tickets" and the filter by person find a ticket for the main assignee and for help alike.
        foreach (var person in new[] { _world.EmpA1, _world.EmpA2 })
        {
            var found = await _world.Get(_world.Admin, $"/api/tickets?assignedToUserId={person.Id}&status=InProgress&pageSize=100");
            Assert.Contains(found.Json.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
        }
    }

    private async Task<(Guid Topic, Guid Plan)> LookupsAsync()
    {
        var topic = new HelpTopic { Name = "AL " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "AL " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db => { db.AddRange(topic, plan); return await db.SaveChangesAsync(); });
        _lookups.Add(topic.Id);
        _lookups.Add(plan.Id);
        return (topic.Id, plan.Id);
    }

    [SqlFact]
    public async Task A_ticket_that_is_opened_is_assigned_to_the_person_it_was_opened_for()
    {
        var (topic, plan) = await LookupsAsync();
        var id = await TicketAsync(null);
        await _world.Db(db => db.Tickets.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TicketStatus.New)));

        var opened = await _world.Post(_world.Admin, $"/api/tickets/{id}/open", new
        {
            source = "TicketSystem",
            helpTopicId = topic,
            departmentId = _world.BranchA,
            subBranchId = (Guid?)null,
            slaPlanId = plan,
            priority = "Medium",
            dueDateUtc = (DateTime?)null,
            assignedToUserId = _world.EmpA2.Id,
            categoryId = (Guid?)null
        });

        Assert.Equal(200, opened.Code);
        Assert.Equal(new[] { (_world.EmpA2.Id, true) }, await ListAsync(id));
        Assert.Equal(_world.EmpA2.Id, await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task A_ticket_made_on_behalf_of_a_client_is_assigned_to_the_person_it_was_made_for()
    {
        var (topic, plan) = await LookupsAsync();

        var created = await _world.Post(_world.Admin, "/api/tickets/on-behalf", new
        {
            clientEmail = _world.CliX.Email,
            title = "on behalf",
            description = "<p>x</p>",
            source = "Phone",
            helpTopicId = topic,
            departmentId = _world.BranchA,
            slaPlanId = plan,
            priority = "Medium",
            assignedToUserId = _world.EmpA2.Id
        });

        Assert.Equal(200, created.Code);
        var id = created.Json.GetProperty("id").GetGuid();
        _tickets.Add(id);
        Assert.Equal(new[] { (_world.EmpA2.Id, true) }, await ListAsync(id));
        Assert.Equal(_world.EmpA2.Id, await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task The_main_assignee_is_the_marked_one_even_when_help_has_been_on_the_ticket_longer()
    {
        var id = await TicketAsync(_world.EmpA1, _world.EmpA2);

        Assert.Equal(_world.EmpA1.Id, await AssignedToInDetailAsync(id));

        var found = await _world.Get(_world.Admin, $"/api/tickets?assignedToUserId={_world.EmpA2.Id}&status=InProgress&pageSize=100");
        var item = found.Json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Contains("EmpA1", item.GetProperty("assignedToName").GetString()!, StringComparison.OrdinalIgnoreCase); // the list names the main one, not the person searched for
    }

    [SqlFact]
    public async Task When_the_main_assignee_leaves_the_one_who_has_been_on_longest_takes_over_and_when_nobody_is_left_the_ticket_is_unassigned()
    {
        var id = await TicketAsync(_world.EmpA1, _world.EmpA2);

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/tickets/{id}/assignees/{_world.EmpA1.Id}")).Code);

        var afterFirst = await ListAsync(id);
        Assert.Equal(new[] { (_world.EmpA2.Id, true) }, afterFirst);
        Assert.Equal(_world.EmpA2.Id, await AssignedToInDetailAsync(id));

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/tickets/{id}/assignees/{_world.EmpA2.Id}")).Code);

        Assert.Empty(await ListAsync(id));
        Assert.Null(await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task When_the_main_assignee_is_deactivated_the_next_one_takes_over()
    {
        var leaver = await _world.CreateActorAsync("assigneeleaver", UserRole.Employee, _world.BranchA, null);
        var id = await TicketAsync(leaver, _world.EmpA2);

        var deactivated = await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{leaver.Id}/active", new { isActive = false });

        Assert.Equal(200, deactivated.Code);
        Assert.Equal(new[] { (_world.EmpA2.Id, true) }, await ListAsync(id));
        Assert.Equal(_world.EmpA2.Id, await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task Removing_help_does_not_change_who_the_ticket_is_assigned_to()
    {
        var id = await TicketAsync(_world.EmpA1, _world.EmpA2);

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/tickets/{id}/assignees/{_world.EmpA2.Id}")).Code);

        Assert.Equal(new[] { (_world.EmpA1.Id, true) }, await ListAsync(id));
        Assert.Equal(_world.EmpA1.Id, await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task The_first_person_added_to_a_ticket_nobody_is_on_becomes_the_main_assignee_and_the_next_one_is_help()
    {
        var id = await TicketAsync(null);

        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/tickets/{id}/assignees", new { userId = _world.EmpA1.Id })).Code);
        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/tickets/{id}/assignees", new { userId = _world.EmpA2.Id })).Code);

        var list = await ListAsync(id);
        Assert.Equal(2, list.Count);
        Assert.Equal(_world.EmpA1.Id, list.Single(x => x.Primary).User);
        Assert.Equal(_world.EmpA1.Id, await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task Two_people_added_as_the_first_assignee_at_the_same_moment_never_make_two_main_assignees_or_a_server_error()
    {
        for (var round = 0; round < 5; round++)
        {
            var id = await TicketAsync(null);

            var both = await Task.WhenAll(
                _world.Post(_world.Admin, $"/api/tickets/{id}/assignees", new { userId = _world.EmpA1.Id }),
                _world.Post(_world.Admin, $"/api/tickets/{id}/assignees", new { userId = _world.EmpA2.Id }));

            Assert.All(both, r => Assert.Contains(r.Code, new[] { 200, 409 }));
            Assert.Contains(both, r => r.Code == 200);
            var list = await ListAsync(id);
            Assert.Equal(both.Count(r => r.Code == 200), list.Count);
            Assert.Single(list, x => x.Primary);
        }
    }

    [SqlFact]
    public async Task Moving_a_ticket_to_another_branch_leaves_it_with_nobody_on_it()
    {
        var id = await TicketAsync(_world.EmpA1, _world.EmpA2);

        var moved = await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB });

        Assert.Equal(200, moved.Code);
        Assert.Empty(await ListAsync(id));
        Assert.Null(await AssignedToInDetailAsync(id));
    }

    [SqlFact]
    public async Task The_database_itself_refuses_a_second_main_assignee()
    {
        var id = await TicketAsync(_world.EmpA1, _world.EmpA2);

        var refused = await Assert.ThrowsAnyAsync<Exception>(() =>
            _world.Db(async db =>
            {
                await db.TicketAssignees.Where(a => a.TicketId == id && a.UserId == _world.EmpA2.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsPrimary, true));
                return 0;
            }));
        Assert.Contains("UX_TicketAssignees_OnePrimaryPerTicket", refused.ToString());
    }
}
