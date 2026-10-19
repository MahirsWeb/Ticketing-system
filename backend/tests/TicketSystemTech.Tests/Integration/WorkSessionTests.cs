using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// T-33: a ticket has any number of work sessions; they belong to the person it is assigned to; people added as extra
/// help log no time of their own and so never appear on a timeline for it. Each test makes (and afterwards removes)
/// tickets of its own, so the shared world the other tests rely on is left exactly as it was.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkSessionTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _created = new();

    public WorkSessionTests(ApiWorld world) => _world = world;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_created.Count == 0) return;
        // Sessions, assignees, messages and knowledge-base chunks go with the ticket (cascade).
        await _world.Db(db => db.Tickets.Where(t => _created.Contains(t.Id)).ExecuteDeleteAsync());
    }

    private static DateTime Yesterday(int hour, int minute = 0) => DateTime.UtcNow.Date.AddDays(-1).AddHours(hour).AddMinutes(minute);

    private async Task<Guid> NewTicketAsync(Actor? mainWorker, params Actor[] helpers)
    {
        var ticket = new Ticket
        {
            TicketNumber = "WS-" + Guid.NewGuid().ToString("N")[..8], Title = "Work session test", Description = "<p>x</p>",
            ClientId = _world.CliX.Id, CompanyId = _world.CompanyId, Status = TicketStatus.InProgress, DepartmentId = _world.BranchA,
            AssignedToUserId = mainWorker?.Id, OpenedAtUtc = DateTime.UtcNow.AddDays(-3), Priority = TicketPriority.Medium, Source = TicketSource.TicketSystem
        };
        if (mainWorker is not null) ticket.Assignees.Add(new TicketAssignee { UserId = mainWorker.Id });
        foreach (var helper in helpers) ticket.Assignees.Add(new TicketAssignee { UserId = helper.Id });

        await _world.Db(async db =>
        {
            db.Tickets.Add(ticket);
            return await db.SaveChangesAsync();
        });
        _created.Add(ticket.Id);
        return ticket.Id;
    }

    private Task<ApiResponse> AddSession(Actor who, Guid ticketId, DateTime start, DateTime? end) =>
        _world.Post(who, $"/api/tickets/{ticketId}/work-sessions", new { startedAtUtc = start, endedAtUtc = end });

    private static List<System.Text.Json.JsonElement> Sessions(ApiResponse detail) =>
        detail.Json.GetProperty("workSessions").EnumerateArray().ToList();

    private Task<List<WorkSession>> StoredSessions(Guid ticketId) =>
        _world.Db(db => db.TicketWorkSessions.AsNoTracking().Where(s => s.TicketId == ticketId).OrderBy(s => s.StartedAtUtc)
            .Select(s => new WorkSession(s.Id, s.UserId, s.StartedAtUtc, s.EndedAtUtc)).ToListAsync());

    private sealed record WorkSession(Guid Id, Guid UserId, DateTime Start, DateTime? End);

    // ---------------- several sessions ----------------

    [SqlFact]
    public async Task A_ticket_can_have_several_sessions_and_none_overwrites_another()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);

        var morning = await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(10));
        var afternoon = await AddSession(_world.EmpA1, ticket, Yesterday(14), Yesterday(15, 30));
        var today = await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddMinutes(-90), DateTime.UtcNow.AddMinutes(-30));

        Assert.Equal(200, morning.Code);
        Assert.Equal(200, afternoon.Code);
        Assert.Equal(200, today.Code);

        var stored = await StoredSessions(ticket);
        Assert.Equal(3, stored.Count);
        Assert.All(stored, s => Assert.Equal(_world.EmpA1.Id, s.UserId));

        // and the ticket shows them all, oldest first
        var detail = await _world.Get(_world.EmpA1, $"/api/tickets/{ticket}");
        Assert.Equal(3, Sessions(detail).Count);
        Assert.Equal(stored.Select(s => s.Id.ToString()), Sessions(detail).Select(s => s.GetProperty("id").GetString()!));
    }

    [SqlFact]
    public async Task An_old_style_pair_of_columns_no_longer_exists_in_the_answer()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);

        var detail = await _world.Get(_world.EmpA1, $"/api/tickets/{ticket}");

        Assert.False(detail.Json.TryGetProperty("workStartedAtUtc", out _));
        Assert.False(detail.Json.TryGetProperty("workEndedAtUtc", out _));
        Assert.Empty(Sessions(detail));
    }

    // ---------------- the helper has no sessions ----------------

    [SqlFact]
    public async Task Whoever_logs_the_time_it_is_the_main_workers_never_the_helpers()
    {
        var ticket = await NewTicketAsync(_world.EmpA1, _world.EmpA2); // EmpA1 works it, EmpA2 helps

        Assert.Equal(200, (await AddSession(_world.EmpA2, ticket, Yesterday(9), Yesterday(10))).Code); // the helper types it in
        Assert.Equal(200, (await AddSession(_world.Admin, ticket, Yesterday(11), Yesterday(12))).Code); // so does an admin

        var stored = await StoredSessions(ticket);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, s => Assert.Equal(_world.EmpA1.Id, s.UserId));
        Assert.DoesNotContain(stored, s => s.UserId == _world.EmpA2.Id);
    }

    [SqlFact]
    public async Task The_timeline_shows_the_main_worker_on_the_ticket_and_not_the_helper()
    {
        var ticket = await NewTicketAsync(_world.EmpA1, _world.EmpA2);
        await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(10));
        var day = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");

        var worker = await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt?userId={_world.EmpA1.Id}&date={day}");
        var helper = await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt?userId={_world.EmpA2.Id}&date={day}");

        Assert.Equal(200, worker.Code);
        var ticketBlocks = worker.Json.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("type").GetString() == "Ticket").ToList();
        Assert.Single(ticketBlocks);
        Assert.Equal(Yesterday(9), ticketBlocks[0].GetProperty("startedAtUtc").GetDateTime().ToUniversalTime());

        Assert.Equal(200, helper.Code);
        Assert.DoesNotContain(helper.Json.GetProperty("entries").EnumerateArray(), e => e.GetProperty("type").GetString() == "Ticket");
    }

    [SqlFact]
    public async Task A_ticket_nobody_logged_time_on_still_shows_for_its_main_worker_only()
    {
        var ticket = await NewTicketAsync(_world.EmpA1, _world.EmpA2); // opened three days ago, no session
        var day = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");

        var worker = await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt?userId={_world.EmpA1.Id}&date={day}");
        var helper = await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt?userId={_world.EmpA2.Id}&date={day}");

        Assert.Contains(worker.Json.GetProperty("entries").EnumerateArray(), e => e.GetProperty("id").GetGuid() == ticket);
        Assert.DoesNotContain(helper.Json.GetProperty("entries").EnumerateArray(), e => e.GetProperty("id").GetGuid() == ticket);
    }

    // ---------------- what is refused ----------------

    [SqlFact]
    public async Task Overlaps_backwards_times_and_the_future_are_refused()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);
        Assert.Equal(200, (await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(11))).Code);

        Assert.Equal(400, (await AddSession(_world.EmpA1, ticket, Yesterday(10), Yesterday(12))).Code); // overlaps
        Assert.Equal(400, (await AddSession(_world.EmpA1, ticket, Yesterday(15), Yesterday(14))).Code); // ends before it starts
        Assert.Equal(400, (await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddHours(3), null)).Code); // starts in the future
        Assert.Equal(200, (await AddSession(_world.EmpA1, ticket, Yesterday(11), Yesterday(12))).Code); // touching is fine

        Assert.Equal(2, (await StoredSessions(ticket)).Count);
    }

    [SqlFact]
    public async Task Only_one_session_can_be_running_at_a_time()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);
        Assert.Equal(200, (await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddHours(-2), null)).Code);

        var second = await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddHours(-1), null);

        Assert.Equal(400, second.Code);
        Assert.Contains("still running", second.Text);
    }

    [SqlFact]
    public async Task Time_cannot_be_logged_on_a_ticket_nobody_is_assigned_to()
    {
        var ticket = await NewTicketAsync(null);

        var response = await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(10));

        Assert.Equal(400, response.Code);
        Assert.Contains("Assign the ticket", response.Text);
    }

    // ---------------- changing and deleting ----------------

    [SqlFact]
    public async Task A_session_can_be_corrected_and_deleted_and_each_change_leaves_an_audit_row()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);
        await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(10));
        await AddSession(_world.EmpA1, ticket, Yesterday(14), Yesterday(15));
        var first = (await StoredSessions(ticket))[0];

        var corrected = await _world.Send(_world.EmpA2, HttpMethod.Put, $"/api/tickets/{ticket}/work-sessions/{first.Id}",
            new { startedAtUtc = Yesterday(8, 30), endedAtUtc = Yesterday(10, 15) });
        var clash = await _world.Send(_world.EmpA1, HttpMethod.Put, $"/api/tickets/{ticket}/work-sessions/{first.Id}",
            new { startedAtUtc = Yesterday(9), endedAtUtc = Yesterday(14, 30) });
        var removed = await _world.Send(_world.EmpA1, HttpMethod.Delete, $"/api/tickets/{ticket}/work-sessions/{first.Id}");

        Assert.Equal(200, corrected.Code);
        Assert.Equal(400, clash.Code);
        Assert.Equal(200, removed.Code);

        var remaining = await StoredSessions(ticket);
        Assert.Single(remaining);
        Assert.Equal(Yesterday(14), remaining[0].Start);
        Assert.Equal(_world.EmpA1.Id, remaining[0].UserId); // a colleague's correction did not take the session over

        var actions = await _world.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == ticket).Select(a => a.Action).ToListAsync());
        Assert.Contains("WorkSessionAdded", actions);
        Assert.Contains("WorkSessionChanged", actions);
        Assert.Contains("WorkSessionDeleted", actions);
    }

    [SqlFact]
    public async Task A_session_id_from_another_ticket_is_not_found()
    {
        var mine = await NewTicketAsync(_world.EmpA1);
        var other = await NewTicketAsync(_world.EmpA1);
        await AddSession(_world.EmpA1, other, Yesterday(9), Yesterday(10));
        var foreign = (await StoredSessions(other))[0];

        Assert.Equal(404, (await _world.Send(_world.EmpA1, HttpMethod.Delete, $"/api/tickets/{mine}/work-sessions/{foreign.Id}")).Code);
        Assert.Equal(404, (await _world.Send(_world.EmpA1, HttpMethod.Put, $"/api/tickets/{mine}/work-sessions/{foreign.Id}", new { startedAtUtc = Yesterday(1) })).Code);
        Assert.Single(await StoredSessions(other));
    }

    // ---------------- work that stops without anybody pressing "end" ----------------

    [SqlFact]
    public async Task Closing_the_ticket_ends_a_running_session()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);
        await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddHours(-2), null);

        var closed = await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/close", new { resolutionSummary = "done" });

        Assert.Equal(200, closed.Code);
        var session = Assert.Single(await StoredSessions(ticket));
        Assert.NotNull(session.End);
        Assert.InRange(session.End!.Value, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
    }

    [SqlFact]
    public async Task Taking_the_main_worker_off_hands_the_work_to_the_helper_and_keeps_the_history()
    {
        var ticket = await NewTicketAsync(_world.EmpA1, _world.EmpA2);
        await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(10));
        await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddHours(-1), null); // running

        var removed = await _world.Send(_world.EmpA2, HttpMethod.Delete, $"/api/tickets/{ticket}/assignees/{_world.EmpA1.Id}");
        Assert.Equal(200, removed.Code);

        var afterHandOver = await StoredSessions(ticket);
        Assert.Equal(2, afterHandOver.Count);
        Assert.All(afterHandOver, s => Assert.Equal(_world.EmpA1.Id, s.UserId)); // earlier work stays with who did it
        Assert.All(afterHandOver, s => Assert.NotNull(s.End)); // the running one was ended

        // the helper is now the main worker, and new time goes to them (from the hand-over on: the old session ended just now)
        Assert.Equal(200, (await AddSession(_world.EmpA2, ticket, DateTime.UtcNow.AddMinutes(1), null)).Code);
        var latest = (await StoredSessions(ticket)).Last();
        Assert.Equal(_world.EmpA2.Id, latest.UserId);
    }

    [SqlFact]
    public async Task Transferring_the_ticket_to_another_branch_ends_a_running_session()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);
        await AddSession(_world.EmpA1, ticket, DateTime.UtcNow.AddHours(-1), null);

        var moved = await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/transfer-branch", new { departmentId = _world.BranchB });

        Assert.Equal(200, moved.Code);
        Assert.NotNull(Assert.Single(await StoredSessions(ticket)).End);
    }

    [SqlFact]
    public async Task Deactivating_a_person_ends_their_running_sessions()
    {
        var leaver = await _world.CreateActorAsync("sessionleaver", UserRole.Employee, _world.BranchA, null);
        var ticket = await NewTicketAsync(leaver, _world.EmpA2);
        await AddSession(leaver, ticket, DateTime.UtcNow.AddHours(-1), null);

        var deactivated = await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{leaver.Id}/active", new { isActive = false });

        Assert.Equal(200, deactivated.Code);
        Assert.NotNull(Assert.Single(await StoredSessions(ticket)).End);
    }

    // ---------------- who sees them ----------------

    [SqlFact]
    public async Task The_client_never_sees_how_long_anybody_worked()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);
        await AddSession(_world.EmpA1, ticket, Yesterday(9), Yesterday(10));

        var asStaff = await _world.Get(_world.EmpA1, $"/api/tickets/{ticket}");
        var asClient = await _world.Get(_world.CliX, $"/api/tickets/{ticket}");

        Assert.Single(Sessions(asStaff));
        Assert.Equal(200, asClient.Code);
        Assert.Empty(Sessions(asClient));
    }

    [SqlFact]
    public async Task A_client_cannot_log_or_change_time()
    {
        var ticket = await NewTicketAsync(_world.EmpA1);

        Assert.Equal(403, (await AddSession(_world.CliX, ticket, Yesterday(9), Yesterday(10))).Code);
        Assert.Empty(await StoredSessions(ticket));
    }
}
