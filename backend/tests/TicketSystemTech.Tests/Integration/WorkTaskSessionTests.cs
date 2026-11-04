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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.WorkTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync(); // its sessions go with it
            await db.TicketWorkSessions.Where(s => _tickets.Contains(s.TicketId)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> NewTaskAsync(Actor worker)
    {
        var task = new WorkTask
        {
            Title = "Session task " + Guid.NewGuid().ToString("N")[..6],
            Description = "d",
            DepartmentId = _world.BranchA,
            CreatedByUserId = worker.Id,
            AssignedByUserId = worker.Id,
            AssignedToUserId = worker.Id
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
            AssignedToUserId = _world.EmpA1.Id,
            OpenedAtUtc = DateTime.UtcNow.AddDays(-3)
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);

        var today = (await GanttAsync(_world.EmpA1, Day(DateTime.UtcNow))).GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
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
            AssignedToUserId = _world.EmpA1.Id,
            OpenedAtUtc = new DateTime(2026, 1, 12, 8, 0, 0, DateTimeKind.Utc),
            ClosedAtUtc = new DateTime(2026, 1, 12, 12, 0, 0, DateTimeKind.Utc)
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);

        var entry = (await GanttAsync(_world.EmpA1, "2026-01-12")).GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == ticket.Id);

        Assert.False(entry.GetProperty("isLogged").GetBoolean());
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
}
