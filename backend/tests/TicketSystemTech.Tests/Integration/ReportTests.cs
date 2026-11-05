using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// What the reports count (T-32, T-15). The tickets live in May 2036, a month no other test uses, so every figure below is exactly
/// what is put in here whatever else is in the database:
/// <code>
///   T1 branch A  created 05-02  closed 05-02 by A1 (4 h)    due 05-03 (in time)   topic 1, A1 on it
///   T2 branch A  created 05-05  closed 05-07 by A2 (48 h)   due 05-06 (late)       topic 1
///   T3 branch A  created 05-12  in progress                                        topic 2, A1 on it
///   T4 branch A  created 05-15  new
///   T5 branch B  created 05-08  closed 05-09 by B1          no due date
///   T6 branch A  created 04-29  closed 05-10 by A1          opened before the range, no times
/// </code>
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportTests : IAsyncLifetime
{
    private const string Range = "from=2036-05-01&to=2036-05-31";

    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _topics = new();
    private HelpTopic _topic1 = null!;
    private HelpTopic _topic2 = null!;

    public ReportTests(ApiWorld world) => _world = world;

    private static DateTime May(int day, int hour = 10) => new(2036, 5, day, hour, 0, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        _topic1 = new HelpTopic { Name = "Report topic one " + Guid.NewGuid().ToString("N")[..6] };
        _topic2 = new HelpTopic { Name = "Report topic two " + Guid.NewGuid().ToString("N")[..6] };
        await _world.Db(async db => { db.AddRange(_topic1, _topic2); return await db.SaveChangesAsync(); });
        _topics.AddRange(new[] { _topic1.Id, _topic2.Id });

        await AddAsync("T1", _world.BranchA, TicketStatus.Closed, created: May(2), opened: May(2), closed: May(2, 14), closedBy: _world.EmpA1, due: May(3), topic: _topic1, assignee: _world.EmpA1);
        await AddAsync("T2", _world.BranchA, TicketStatus.Closed, created: May(5), opened: May(5), closed: May(7), closedBy: _world.EmpA2, due: May(6), topic: _topic1);
        await AddAsync("T3", _world.BranchA, TicketStatus.InProgress, created: May(12), opened: May(12), topic: _topic2, assignee: _world.EmpA1);
        await AddAsync("T4", _world.BranchA, TicketStatus.New, created: May(15));
        await AddAsync("T5", _world.BranchB, TicketStatus.Closed, created: May(8), opened: May(8), closed: May(9), closedBy: _world.EmpB1);
        await AddAsync("T6", _world.BranchA, TicketStatus.Closed, created: new DateTime(2036, 4, 29, 10, 0, 0, DateTimeKind.Utc), closed: May(10), closedBy: _world.EmpA1);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            return await db.HelpTopics.Where(h => _topics.Contains(h.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task AddAsync(string number, Guid branch, TicketStatus status, DateTime created, DateTime? opened = null, DateTime? closed = null,
        Actor? closedBy = null, DateTime? due = null, HelpTopic? topic = null, Actor? assignee = null)
    {
        var ticket = new Ticket
        {
            TicketNumber = "RP-" + number + "-" + Guid.NewGuid().ToString("N")[..6],
            Title = "report " + number,
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = branch,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            CreatedAt = created,
            OpenedAtUtc = opened,
            ClosedAtUtc = closed,
            ClosedByUserId = closedBy?.Id,
            DueDateUtc = due,
            HelpTopicId = topic?.Id,
            ResolutionSummary = closed.HasValue ? "done" : null
        };
        if (assignee is not null) ticket.Assignees.Add(new TicketAssignee { UserId = assignee.Id, IsPrimary = true });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
    }

    [SqlFact]
    public async Task The_summary_counts_what_came_in_and_what_got_done_in_the_range_for_the_staff_members_own_branch()
    {
        var summary = (await _world.Get(_world.EmpA1, $"/api/reports/summary?{Range}")).Json;

        Assert.Equal(1, summary.GetProperty("totalNew").GetInt32());      // T4
        Assert.Equal(1, summary.GetProperty("totalOpen").GetInt32());     // T3
        Assert.Equal(3, summary.GetProperty("totalClosed").GetInt32());   // T1, T2 and T6: closed in May, even though T6 came in before it
        Assert.Equal(26.0, summary.GetProperty("avgResolutionHours").GetDouble()); // (4 + 48) / 2; T6 has no times
        Assert.Equal(50.0, summary.GetProperty("slaComplianceRate").GetDouble());  // T1 in time, T2 late; T6 and T5 have no due date
    }

    [SqlFact]
    public async Task An_admin_sees_every_branch_and_can_narrow_to_one_and_a_staff_member_cannot_widen_theirs()
    {
        var all = (await _world.Get(_world.Admin, $"/api/reports/summary?{Range}")).Json;
        Assert.Equal(4, all.GetProperty("totalClosed").GetInt32());   // T1, T2, T5, T6

        var onlyB = (await _world.Get(_world.Admin, $"/api/reports/summary?{Range}&departmentId={_world.BranchB}")).Json;
        Assert.Equal(1, onlyB.GetProperty("totalClosed").GetInt32()); // T5
        Assert.Equal(0, onlyB.GetProperty("totalNew").GetInt32());

        // asking for the other branch changes nothing for a member of branch A
        var staff = (await _world.Get(_world.EmpA1, $"/api/reports/summary?{Range}&departmentId={_world.BranchB}")).Json;
        Assert.Equal(3, staff.GetProperty("totalClosed").GetInt32());
    }

    [SqlFact]
    public async Task A_figure_with_nothing_to_measure_is_empty_not_perfect()
    {
        var summary = (await _world.Get(_world.EmpB1, $"/api/reports/summary?{Range}")).Json; // branch B: one closed ticket, no due date

        Assert.Equal(1, summary.GetProperty("totalClosed").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, summary.GetProperty("slaComplianceRate").ValueKind);
    }

    [SqlFact]
    public async Task The_agent_filter_means_the_tickets_a_person_is_on()
    {
        var summary = (await _world.Get(_world.Admin, $"/api/reports/summary?{Range}&agentId={_world.EmpA1.Id}")).Json;

        Assert.Equal(0, summary.GetProperty("totalNew").GetInt32());
        Assert.Equal(1, summary.GetProperty("totalOpen").GetInt32());   // T3
        Assert.Equal(1, summary.GetProperty("totalClosed").GetInt32()); // T1
    }

    [SqlFact]
    public async Task The_leaderboard_ranks_who_closed_the_tickets_and_a_chosen_agent_narrows_it_to_them()
    {
        var board = (await _world.Get(_world.EmpA1, $"/api/reports/leaderboard?{Range}")).Json.EnumerateArray()
            .Select(e => (Id: e.GetProperty("userId").GetGuid(), Count: e.GetProperty("closedCount").GetInt32())).ToList();

        Assert.Equal(new[] { (_world.EmpA1.Id, 2), (_world.EmpA2.Id, 1) }, board); // T1 and T6; T2

        var one = (await _world.Get(_world.EmpA1, $"/api/reports/leaderboard?{Range}&agentId={_world.EmpA2.Id}")).Json.EnumerateArray().ToList();
        Assert.Single(one);
        Assert.Equal(_world.EmpA2.Id, one[0].GetProperty("userId").GetGuid());
    }

    [SqlFact]
    public async Task The_time_series_has_a_point_for_every_day_with_opened_and_closed_counted_on_their_own_days()
    {
        var points = (await _world.Get(_world.EmpA1, $"/api/reports/timeseries?{Range}")).Json.EnumerateArray()
            .ToDictionary(p => p.GetProperty("date").GetString()!, p => (Opened: p.GetProperty("opened").GetInt32(), Closed: p.GetProperty("closed").GetInt32()));

        Assert.Equal(31, points.Count);
        Assert.Equal((1, 1), points["2036-05-02"]);   // T1 came in and was closed
        Assert.Equal((1, 0), points["2036-05-05"]);   // T2 came in
        Assert.Equal((0, 1), points["2036-05-07"]);   // T2 was closed two days later
        Assert.Equal((0, 1), points["2036-05-10"]);   // T6 was closed though it came in before the range
        Assert.Equal(4, points.Values.Sum(p => p.Opened));
        Assert.Equal(3, points.Values.Sum(p => p.Closed));
    }

    [SqlFact]
    public async Task The_top_issues_rank_the_help_topics_and_stop_at_the_limit()
    {
        var topics = (await _world.Get(_world.EmpA1, $"/api/reports/top-issues?{Range}")).Json.EnumerateArray()
            .Select(t => (Name: t.GetProperty("helpTopicName").GetString()!, Count: t.GetProperty("count").GetInt32())).ToList();

        Assert.Equal(new[] { (_topic1.Name, 2), (_topic2.Name, 1) }, topics);

        var limited = (await _world.Get(_world.EmpA1, $"/api/reports/top-issues?{Range}&limit=1")).Json.EnumerateArray().ToList();
        Assert.Single(limited);
    }

    [SqlFact]
    public async Task The_branch_breakdown_is_for_admin_only_and_counts_new_open_and_closed_per_branch()
    {
        Assert.Equal(403, (await _world.Get(_world.EmpA1, $"/api/reports/by-branch?{Range}")).Code);

        var rows = (await _world.Get(_world.Admin, $"/api/reports/by-branch?{Range}")).Json.EnumerateArray()
            .ToDictionary(r => r.GetProperty("departmentId").GetGuid(),
                r => (New: r.GetProperty("totalNew").GetInt32(), Open: r.GetProperty("totalOpen").GetInt32(), Closed: r.GetProperty("totalClosed").GetInt32()));

        Assert.Equal((1, 1, 3), rows[_world.BranchA]);
        Assert.Equal((0, 0, 1), rows[_world.BranchB]);
    }

    [SqlFact]
    public async Task Tickets_are_bucketed_by_the_period_asked_for()
    {
        var byMonth = (await _world.Get(_world.EmpA1, $"/api/reports/period-comparison?{Range}&groupBy=month")).Json.EnumerateArray().ToList();
        Assert.Single(byMonth);
        Assert.Equal("May 2036", byMonth[0].GetProperty("label").GetString());
        Assert.Equal(4, byMonth[0].GetProperty("count").GetInt32());

        var byYear = (await _world.Get(_world.EmpA1, $"/api/reports/period-comparison?{Range}&groupBy=year")).Json.EnumerateArray().ToList();
        Assert.Equal("2036", byYear.Single().GetProperty("label").GetString());

        var byWeek = (await _world.Get(_world.EmpA1, $"/api/reports/period-comparison?{Range}&groupBy=week")).Json.EnumerateArray().ToList();
        Assert.True(byWeek.Count > 1);
        Assert.Equal(4, byWeek.Sum(w => w.GetProperty("count").GetInt32()));
    }

    [SqlFact]
    public async Task The_AI_summary_needs_a_month_and_is_built_from_the_topics_of_that_month_only()
    {
        var tooShort = await _world.Post(_world.EmpA1, "/api/reports/ai-insights", new { from = "2036-05-01T00:00:00Z", to = "2036-05-10T00:00:00Z" });
        Assert.Equal(400, tooShort.Code);
        Assert.Contains("at least one month", tooShort.Text);

        var empty = await _world.Post(_world.EmpA1, "/api/reports/ai-insights", new { from = "2036-07-01T00:00:00Z", to = "2036-08-15T00:00:00Z" });
        Assert.Equal(200, empty.Code);
        Assert.Contains("No tickets found", empty.Json.GetProperty("summary").GetString());

        var answered = await _world.Post(_world.EmpA1, "/api/reports/ai-insights", new { from = "2036-04-30T00:00:00Z", to = "2036-06-10T00:00:00Z" });
        Assert.Equal(200, answered.Code);
        var context = _world.Factory.Chat.LastContext!;
        Assert.Contains("Total tickets in period: 4", context); // T1 to T4: T6 came in on 04-29, before the period, and T5 is branch B's
        Assert.Contains(_topic1.Name, context);
    }

    [SqlFact]
    public async Task The_export_is_a_workbook_with_one_row_per_ticket_of_the_staff_members_branch()
    {
        var file = await _world.GetBytes(_world.EmpA1, $"/api/reports/export?{Range}");

        Assert.Equal(200, file.Code);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.StartsWith("tickets-export-", file.FileName);
        using var book = new XSSFWorkbook(new MemoryStream(file.Bytes));
        Assert.Equal(4, book.GetSheet("Tickets").LastRowNum); // header + T1..T4
    }
}
