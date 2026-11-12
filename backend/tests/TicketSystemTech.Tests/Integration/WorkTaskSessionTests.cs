using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Work on a task is a list of sessions, like work on a ticket (TS-33.1): starting a task again adds a session instead of
/// replacing the last one, and the times that are entered are held to the same rules. Also the timeline built from them
/// (TS-33.3, TS-09.3) and the concurrency token on tasks (TS-35.4).
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkTaskSessionTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tasks = new();
    private readonly List<Guid> _tickets = new();

    public WorkTaskSessionTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.AuditLogs.Where(a => a.EntityId != null && _tasks.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.Message.Contains("Session task")).ExecuteDeleteAsync();
            await db.WorkTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync(); // its sessions go with it
            await db.TicketWorkSessions.Where(s => _tickets.Contains(s.TicketId)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> NewTaskAsync(Actor worker, Actor? assignedBy = null, WorkTaskStatus status = WorkTaskStatus.Pending)
    {
        var task = new WorkTask
        {
            Title = "Session task " + Guid.NewGuid().ToString("N")[..6],
            Description = "d",
            DepartmentId = _world.BranchA,
            CreatedByUserId = (assignedBy ?? worker).Id,
            AssignedByUserId = (assignedBy ?? worker).Id,
            AssignedToUserId = worker.Id,
            Status = status
        };
        await _world.Db(async db => { db.WorkTasks.Add(task); return await db.SaveChangesAsync(); });
        _tasks.Add(task.Id);
        return task.Id;
    }

    private async Task<List<WorkTaskSession>> SessionsOfAsync(Guid task) =>
        await _world.Db(db => db.WorkTaskSessions.AsNoTracking().Where(s => s.WorkTaskId == task).OrderBy(s => s.StartedAtUtc).ToListAsync());

    private Task AddSessionAsync(Guid task, Actor worker, DateTime start, DateTime? end) =>
        _world.Db(async db =>
        {
            db.WorkTaskSessions.Add(new WorkTaskSession { WorkTaskId = task, UserId = worker.Id, StartedAtUtc = start, EndedAtUtc = end });
            return await db.SaveChangesAsync();
        });

    private static object Time(DateTime? start, DateTime? end) => new { startedAtUtc = start, endedAtUtc = end };

    // ======================= starting again =======================

    [SqlFact]
    public async Task Starting_a_task_again_adds_a_session_and_the_first_one_stays()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        var past = DateTime.UtcNow.AddHours(-5);
        await AddSessionAsync(task, _world.EmpA1, past, past.AddHours(2)); // yesterday's work, finished

        var started = await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start");

        Assert.Equal(200, started.Code);
        var sessions = await SessionsOfAsync(task);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(past, sessions[0].StartedAtUtc, TimeSpan.FromMilliseconds(5));
        Assert.Equal(past.AddHours(2), sessions[0].EndedAtUtc!.Value, TimeSpan.FromMilliseconds(5)); // untouched
        Assert.Null(sessions[1].EndedAtUtc);
        Assert.Equal("InProgress", started.Json.GetProperty("status").GetString());
        Assert.Equal(2, started.Json.GetProperty("sessions").GetArrayLength());
    }

    [SqlFact]
    public async Task A_running_timer_cannot_be_started_twice_and_stopping_ends_only_that_session()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start")).Code);
        Assert.Equal(400, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start")).Code);

        var stopped = await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/stop");

        Assert.Equal(200, stopped.Code);
        Assert.Equal("Done", stopped.Json.GetProperty("status").GetString());
        Assert.All(await SessionsOfAsync(task), s => Assert.NotNull(s.EndedAtUtc));
        Assert.Equal(400, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/stop")).Code); // nothing is running now
    }

    [SqlFact]
    public async Task Only_the_person_the_task_is_assigned_to_runs_its_timer()
    {
        var task = await NewTaskAsync(_world.EmpA1);

        Assert.Equal(403, (await _world.Post(_world.EmpA2, $"/api/work-tasks/{task}/start")).Code);
    }

    [SqlFact]
    public async Task Reassigning_a_task_stops_the_previous_owners_running_timer()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start")).Code);

        // hand it to a colleague while the timer is still running
        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/reassign", new { assignedToUserId = _world.EmpA2.Id })).Code);

        // the previous owner's session is closed, not left running for ever (it used to stay open: the old owner got a 403 on
        // Stop, the new owner got "Timer is already running", and it blocked the old owner's other work)
        Assert.All(await SessionsOfAsync(task), s => Assert.NotNull(s.EndedAtUtc));
        // and the new owner can start their own stretch
        Assert.Equal(200, (await _world.Post(_world.EmpA2, $"/api/work-tasks/{task}/start")).Code);
    }

    [SqlFact]
    public async Task Only_the_worker_the_assignee_or_an_admin_may_change_a_recorded_stretch()
    {
        var task = await NewTaskAsync(_world.EmpA1); // assigned to, and worked by, EmpA1
        var past = DateTime.UtcNow.AddHours(-5);
        await AddSessionAsync(task, _world.EmpA1, past, past.AddHours(1));

        // a colleague who merely shares the branch may NOT move someone else's recorded time (it used to be allowed)
        var byColleague = await _world.Send(_world.EmpA2, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(past, past.AddHours(2)));
        Assert.Equal(403, byColleague.Code);

        // the person who did the work (here also the current assignee) may
        Assert.Equal(200, (await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(past, past.AddHours(2)))).Code);

        // the change is written to the audit log, with what the time WAS as well as what it became (a dispute needs both)
        var details = await _world.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == task && a.Action == "WorkTaskSessionChanged").Select(a => a.Details).SingleAsync());
        var parts = details!.Split(" -> ");
        Assert.Equal(2, parts.Length);
        Assert.Contains(past.ToString("O")[..19], parts[0]);              // the old stretch: started at `past` ...
        Assert.Contains(past.AddHours(1).ToString("O")[..19], parts[0]);  // ... and ended an hour later
        Assert.Contains(past.AddHours(2).ToString("O")[..19], parts[1]);  // the new one ends two hours after the start
    }

    // ======================= entering a time by hand =======================

    [SqlFact]
    public async Task A_time_in_the_future_or_a_stretch_of_months_is_refused()
    {
        var task = await NewTaskAsync(_world.EmpA1);

        // the tester's request: 2030-01-01 to 2030-03-01
        var future = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time",
            Time(new DateTime(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2030, 3, 1, 8, 0, 0, DateTimeKind.Utc)));
        var months = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time",
            Time(DateTime.UtcNow.AddDays(-70), DateTime.UtcNow.AddDays(-10)));

        Assert.Equal(400, future.Code);
        Assert.Contains("future", future.Text);
        Assert.Equal(400, months.Code);
        Assert.Contains("24 hours", months.Text);
        Assert.Empty(await SessionsOfAsync(task));
    }

    [SqlFact]
    public async Task An_end_before_the_start_or_an_end_without_a_start_is_refused()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        var now = DateTime.UtcNow;

        Assert.Equal(400, (await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(now.AddHours(-1), now.AddHours(-2)))).Code);
        Assert.Equal(400, (await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(null, now.AddHours(-2)))).Code);
    }

    [SqlFact]
    public async Task A_correction_changes_the_latest_session_and_never_an_earlier_one_and_may_not_overlap_it()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        var now = DateTime.UtcNow;
        await AddSessionAsync(task, _world.EmpA1, now.AddHours(-10), now.AddHours(-8));
        await AddSessionAsync(task, _world.EmpA1, now.AddHours(-5), now.AddHours(-4));

        var overlapping = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(now.AddHours(-9), now.AddHours(-4)));
        Assert.Equal(400, overlapping.Code);
        Assert.Contains("overlaps", overlapping.Text);

        var fine = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(now.AddHours(-6), now.AddHours(-3)));
        Assert.Equal(200, fine.Code);

        var sessions = await SessionsOfAsync(task);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(now.AddHours(-8), sessions[0].EndedAtUtc!.Value, TimeSpan.FromMilliseconds(5)); // the earlier one is as it was
        Assert.Equal(now.AddHours(-6), sessions[1].StartedAtUtc, TimeSpan.FromMilliseconds(5));
    }

    [SqlFact]
    public async Task Entering_a_time_on_a_task_that_has_none_creates_its_first_session_and_clearing_it_removes_it()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        var now = DateTime.UtcNow;

        var entered = await _world.Send(_world.EmpA2, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(now.AddHours(-3), now.AddHours(-2))); // a colleague may correct it
        Assert.Equal(200, entered.Code);
        Assert.Equal("Done", entered.Json.GetProperty("status").GetString());
        Assert.Equal(_world.EmpA1.Id, Assert.Single(await SessionsOfAsync(task)).UserId); // the work is the assignee's

        var cleared = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{task}/time", Time(null, null));
        Assert.Equal(200, cleared.Code);
        Assert.Equal("Pending", cleared.Json.GetProperty("status").GetString());
        Assert.Empty(await SessionsOfAsync(task));
    }

    [SqlFact]
    public async Task The_list_summarises_the_sessions_as_first_start_and_last_end_and_has_no_end_while_one_runs()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        var now = DateTime.UtcNow;
        await AddSessionAsync(task, _world.EmpA1, now.AddHours(-9), now.AddHours(-8));
        await AddSessionAsync(task, _world.EmpA1, now.AddHours(-3), now.AddHours(-2));

        JsonRow Row(ApiResponse r) => new(r.Json.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == task));
        var finished = Row(await _world.Get(_world.EmpA1, "/api/work-tasks"));
        Assert.Equal(now.AddHours(-9), finished.Start, TimeSpan.FromSeconds(1));
        Assert.Equal(now.AddHours(-2), finished.End!.Value, TimeSpan.FromSeconds(1));

        await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start");
        Assert.Null(Row(await _world.Get(_world.EmpA1, "/api/work-tasks")).End);
    }

    private sealed record JsonRow(System.Text.Json.JsonElement Element)
    {
        public DateTime Start => Element.GetProperty("startedAtUtc").GetDateTime();
        public DateTime? End => Element.GetProperty("endedAtUtc").ValueKind == System.Text.Json.JsonValueKind.Null ? null : Element.GetProperty("endedAtUtc").GetDateTime();
    }

    // ======================= the timeline =======================

    private static string Day(DateTime date) => date.ToString("yyyy-MM-dd");

    private async Task<System.Text.Json.JsonElement> GanttAsync(Actor person, string date) =>
        (await _world.Get(_world.Admin, $"/api/work-tasks/gantt?userId={person.Id}&date={date}")).Json;

    [SqlFact]
    public async Task Every_session_is_its_own_block_on_the_timeline()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        var morning = new DateTime(2026, 1, 12, 8, 0, 0, DateTimeKind.Utc);   // 09:00-10:00 in Sarajevo
        var afternoon = new DateTime(2026, 1, 12, 13, 0, 0, DateTimeKind.Utc);
        await AddSessionAsync(task, _world.EmpA1, morning, morning.AddHours(1));
        await AddSessionAsync(task, _world.EmpA1, afternoon, afternoon.AddHours(1));

        var entries = (await GanttAsync(_world.EmpA1, "2026-01-12")).GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("id").GetGuid() == task).ToList();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("WorkTask", e.GetProperty("type").GetString()));
        Assert.All(entries, e => Assert.True(e.GetProperty("isLogged").GetBoolean()));
    }

    [SqlFact]
    public async Task A_day_on_the_timeline_is_the_business_days_local_calendar_day_not_the_UTC_one()
    {
        // 23:30 UTC on 15 January is 00:30 on 16 January in Sarajevo (UTC+1 in winter)
        var task = await NewTaskAsync(_world.EmpA1);
        var late = new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc);
        await AddSessionAsync(task, _world.EmpA1, late, late.AddMinutes(30));

        var on15 = (await GanttAsync(_world.EmpA1, "2026-01-15")).GetProperty("entries").EnumerateArray().Any(e => e.GetProperty("id").GetGuid() == task);
        var on16 = (await GanttAsync(_world.EmpA1, "2026-01-16")).GetProperty("entries").EnumerateArray().Any(e => e.GetProperty("id").GetGuid() == task);

        Assert.False(on15);
        Assert.True(on16);
    }

    [SqlFact]
    public async Task A_day_that_has_not_happened_yet_is_empty_even_with_an_open_ticket_and_a_running_timer()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start"); // running now
        var ticket = new Ticket
        {
            TicketNumber = "GT-" + Guid.NewGuid().ToString("N")[..8],
            Title = "open and unlogged",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            Assignees = { new TicketAssignee { UserId = _world.EmpA1.Id, IsPrimary = true } },
            OpenedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);

        // "Today" is the day in the business time zone, which is how the application reads a day: between midnight and two o'clock local time the UTC date is still yesterday's.
        var businessToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TicketSystemTech.Application.Sla.BusinessTimeZone.Resolve(new TicketSystemTech.Application.Common.Options.SlaOptions().TimeZoneId));
        var today = (await GanttAsync(_world.EmpA1, Day(businessToday))).GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        var future = (await GanttAsync(_world.EmpA1, "2027-06-30")).GetProperty("entries").GetArrayLength();

        Assert.Contains(task, today);
        Assert.Contains(ticket.Id, today);
        Assert.Equal(0, future);
    }

    [SqlFact]
    public async Task A_ticket_nobody_logged_time_on_is_marked_as_elapsed_time_not_as_work()
    {
        var ticket = new Ticket
        {
            TicketNumber = "GT-" + Guid.NewGuid().ToString("N")[..8],
            Title = "unlogged",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.Closed,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            Assignees = { new TicketAssignee { UserId = _world.EmpA1.Id, IsPrimary = true } },
            OpenedAtUtc = new DateTime(2026, 1, 12, 8, 0, 0, DateTimeKind.Utc),
            ClosedAtUtc = new DateTime(2026, 1, 12, 12, 0, 0, DateTimeKind.Utc)
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);

        var entry = (await GanttAsync(_world.EmpA1, "2026-01-12")).GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == ticket.Id);

        Assert.False(entry.GetProperty("isLogged").GetBoolean());
    }

    // ======================= one person, one thing at a time (NOVO-DOM-01) =======================

    private static DateTime Yesterday(int hour, int minute = 0) => DateTime.UtcNow.Date.AddDays(-1).AddHours(hour).AddMinutes(minute);

    private async Task<Guid> NewTicketWorkedByAsync(Actor worker)
    {
        var ticket = new Ticket
        {
            TicketNumber = "OW-" + Guid.NewGuid().ToString("N")[..8],
            Title = "overlap test",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            OpenedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        ticket.Assignees.Add(new TicketAssignee { UserId = worker.Id, IsPrimary = true });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private Task<ApiResponse> TicketSession(Actor who, Guid ticket, DateTime start, DateTime? end) =>
        _world.Post(who, $"/api/tickets/{ticket}/work-sessions", new { startedAtUtc = start, endedAtUtc = end });

    private Task<Guid> LatestSessionIdOfTicketAsync(Guid ticket) =>
        _world.Db(db => db.TicketWorkSessions.AsNoTracking().Where(s => s.TicketId == ticket).OrderByDescending(s => s.StartedAtUtc).Select(s => s.Id).FirstAsync());

    [SqlFact]
    public async Task One_person_cannot_work_two_tickets_at_the_same_time()
    {
        var first = await NewTicketWorkedByAsync(_world.EmpA1);
        var second = await NewTicketWorkedByAsync(_world.EmpA1);

        var a = await TicketSession(_world.EmpA1, first, Yesterday(9), Yesterday(11));
        var overlapping = await TicketSession(_world.EmpA1, second, Yesterday(10), Yesterday(12));
        var after = await TicketSession(_world.EmpA1, second, Yesterday(11), Yesterday(12));
        var moved = await _world.Send(_world.EmpA1, HttpMethod.Put, $"/api/tickets/{second}/work-sessions/{await LatestSessionIdOfTicketAsync(second)}",
            new { startedAtUtc = Yesterday(10, 30), endedAtUtc = Yesterday(12) });

        Assert.Equal(200, a.Code);
        Assert.Equal(400, overlapping.Code);
        Assert.Contains("another ticket or task", overlapping.Text);
        Assert.Equal(200, after.Code);             // starting the minute the other one ended is fine
        Assert.Equal(400, moved.Code);             // and so is correcting a session until it runs into the other
    }

    [SqlFact]
    public async Task Somebody_elses_work_never_gets_in_the_way()
    {
        var mine = await NewTicketWorkedByAsync(_world.EmpA1);
        var theirs = await NewTicketWorkedByAsync(_world.EmpA2);

        var first = await TicketSession(_world.EmpA1, mine, Yesterday(9), Yesterday(11));
        var second = await TicketSession(_world.EmpA2, theirs, Yesterday(9), Yesterday(11));

        Assert.Equal(200, first.Code);
        Assert.Equal(200, second.Code);
    }

    [SqlFact]
    public async Task A_task_and_a_ticket_of_the_same_person_cannot_overlap_either_way()
    {
        var task = await NewTaskAsync(_world.EmpA1);
        await AddSessionAsync(task, _world.EmpA1, Yesterday(9), Yesterday(10));
        var ticket = await NewTicketWorkedByAsync(_world.EmpA1);

        var overlappingTicket = await TicketSession(_world.EmpA1, ticket, Yesterday(9, 30), Yesterday(10, 30));
        var afterTask = await TicketSession(_world.EmpA1, ticket, Yesterday(10), Yesterday(11));
        var otherTask = await NewTaskAsync(_world.EmpA1);
        var overlappingTask = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{otherTask}/time", Time(Yesterday(10, 30), Yesterday(11, 30)));
        var cleanTask = await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{otherTask}/time", Time(Yesterday(11), Yesterday(12)));

        Assert.Equal(400, overlappingTicket.Code);
        Assert.Equal(200, afterTask.Code);
        Assert.Equal(400, overlappingTask.Code);
        Assert.Contains("another ticket or task", overlappingTask.Text);
        Assert.Equal(200, cleanTask.Code);
    }

    [SqlFact]
    public async Task A_timer_that_is_still_running_blocks_starting_another_one_and_ending_it_frees_the_person()
    {
        var ticket = await NewTicketWorkedByAsync(_world.EmpA1);
        var task = await NewTaskAsync(_world.EmpA1);

        var running = await TicketSession(_world.EmpA1, ticket, DateTime.UtcNow.AddMinutes(-2), null);
        var blocked = await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start", new { });

        Assert.Equal(200, running.Code);
        Assert.Equal(400, blocked.Code);
        Assert.Contains("still running on another", blocked.Text);

        await _world.Db(db => db.TicketWorkSessions.Where(s => s.TicketId == ticket).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAtUtc, DateTime.UtcNow.AddMinutes(-1))));
        var started = await _world.Post(_world.EmpA1, $"/api/work-tasks/{task}/start", new { });
        var back = await TicketSession(_world.EmpA1, ticket, DateTime.UtcNow.AddSeconds(-30), null);

        Assert.Equal(200, started.Code);
        Assert.Equal(400, back.Code); // and now the task timer is the one that is running
    }

    // ======================= the concurrency token =======================

    [SqlFact]
    public async Task Two_people_changing_the_same_task_at_once_are_not_both_silently_saved()
    {
        var task = await NewTaskAsync(_world.EmpA1);

        using var first = _world.Factory.Services.CreateScope();
        using var second = _world.Factory.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var b = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var seenByA = await a.WorkTasks.SingleAsync(t => t.Id == task);
        var seenByB = await b.WorkTasks.SingleAsync(t => t.Id == task);

        seenByA.AssignedToUserId = _world.EmpA2.Id;
        await a.SaveChangesAsync();
        seenByB.Title = "renamed by the other person";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    // ======================= a person who stops working =======================

    [SqlFact]
    public async Task The_open_tasks_of_a_deactivated_person_go_back_to_who_handed_them_over_and_are_not_left_in_progress()
    {
        var leaver = await _world.CreateActorAsync("taskleaver", UserRole.Employee, _world.BranchA, null);
        var task = await NewTaskAsync(leaver, assignedBy: _world.EmpA1, status: WorkTaskStatus.InProgress);
        await AddSessionAsync(task, leaver, DateTime.UtcNow.AddMinutes(-20), null); // the timer is running

        var deactivated = await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{leaver.Id}/active", new { isActive = false });
        Assert.Equal(200, deactivated.Code);

        var row = await _world.Db(db => db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == task));
        Assert.Equal(_world.EmpA1.Id, row.AssignedToUserId);            // somebody active has it again
        Assert.Equal(WorkTaskStatus.Pending, row.Status);               // and nobody is "working" on it
        Assert.All(await SessionsOfAsync(task), s => Assert.NotNull(s.EndedAtUtc)); // the timer stopped

        var history = await _world.Db(db => db.WorkTaskAssignmentLogs.AsNoTracking().Where(h => h.WorkTaskId == task).ToListAsync());
        Assert.Contains(history, h => h.AssignedToUserId == _world.EmpA1.Id && h.AssignedByUserId == _world.Admin.Id);
        Assert.True(await _world.Db(db => db.AuditLogs.AnyAsync(a => a.EntityId == task && a.Action == "WorkTaskHandedBack")));
        // the colleague who got it back is told, in the list and by mail
        Assert.True(await _world.Db(db => db.Notifications.AnyAsync(n => n.UserId == _world.EmpA1.Id && n.Type == NotificationType.TaskAssigned && n.Message.Contains(row.Title))));
    }

    [SqlFact]
    public async Task A_finished_task_stays_with_the_person_who_did_it_and_a_task_nobody_else_can_take_stays_put()
    {
        var leaver = await _world.CreateActorAsync("taskleaver2", UserRole.Employee, _world.BranchA, null);
        var done = await NewTaskAsync(leaver, assignedBy: _world.EmpA1, status: WorkTaskStatus.Done);
        var selfMade = await NewTaskAsync(leaver, status: WorkTaskStatus.InProgress); // made by the person for themselves: nobody to hand it back to

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{leaver.Id}/active", new { isActive = false })).Code);

        var rows = await _world.Db(db => db.WorkTasks.AsNoTracking().Where(t => t.Id == done || t.Id == selfMade).ToListAsync());
        Assert.All(rows, t => Assert.Equal(leaver.Id, t.AssignedToUserId));

        // It stays on the person, but no longer as work "in progress": it is shown as not started, with a record, for a colleague to reassign.
        Assert.Equal(WorkTaskStatus.Pending, rows.Single(t => t.Id == selfMade).Status);
        Assert.Equal(WorkTaskStatus.Done, rows.Single(t => t.Id == done).Status);
        Assert.True(await _world.Db(db => db.AuditLogs.AnyAsync(a => a.EntityId == selfMade && a.Action == "WorkTaskLeftWithoutOwner")));
    }

    [SqlFact]
    public async Task Moving_a_person_between_sub_branches_of_the_same_branch_leaves_their_tasks_alone()
    {
        var mover = await _world.CreateActorAsync("taskmover", UserRole.Employee, _world.BranchA, null);
        var task = await NewTaskAsync(mover, assignedBy: _world.EmpA1, status: WorkTaskStatus.InProgress);

        // Same role, same branch, only the sub-branch changes (here: to none, which is a change of the sub-branch field).
        var user = await _world.Db(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == mover.Id));
        var sub = await _world.Db(async db =>
        {
            var created = new SubBranch { DepartmentId = _world.BranchA, Name = "Moving-" + Guid.NewGuid().ToString("N")[..6] };
            db.SubBranches.Add(created);
            await db.SaveChangesAsync();
            return created.Id;
        });
        try
        {
            var moved = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/users/{mover.Id}", new
            {
                firstName = user.FirstName,
                lastName = user.LastName,
                role = "Employee",
                departmentId = _world.BranchA,
                subBranchId = sub
            });
            Assert.Equal(200, moved.Code);

            var row = await _world.Db(db => db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == task));
            Assert.Equal(mover.Id, row.AssignedToUserId); // not handed back: the branch is what tasks belong to
        }
        finally
        {
            // A branch with a sub-branch makes every later ticket and account of that branch ask for one: put things back.
            await _world.Db(async db =>
            {
                await db.Users.Where(u => u.Id == mover.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SubBranchId, (Guid?)null));
                return await db.SubBranches.Where(x => x.Id == sub).ExecuteDeleteAsync();
            });
        }
    }
}
