using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The reports of the "Tickets" tab: age and standstill, the time to solve (median and 90th percentile), the first reply of staff, the transfers between
/// branches. A test that needs its figures counted by hand makes a branch of its own (Admin may narrow to it) and, where a period is asked, a year of its own.
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketInsightsTests : IAsyncLifetime
{
    private const string Base = "/api/reports/tickets";

    private readonly ApiWorld _world;
    private readonly List<Guid> _departments = new();

    public TicketInsightsTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            var ids = await db.Tickets.Where(t => t.TicketNumber.StartsWith("TI-")).Select(t => t.Id).ToListAsync();
            await db.AuditLogs.Where(a => a.EntityId != null && ids.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => t.TicketNumber.StartsWith("TI-")).ExecuteDeleteAsync(); // messages, assignees and work sessions go with their tickets
            return await db.Departments.Where(d => _departments.Contains(d.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> DepartmentAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var department = new Department { Name = "TI " + tag, Email = $"ti-{tag}@insights.test" };
        await _world.Db(async db => { db.Departments.Add(department); return await db.SaveChangesAsync(); });
        _departments.Add(department.Id);
        return department.Id;
    }

    private static DateTime Ago(double days) => DateTime.UtcNow.AddDays(-days);

    private Ticket Made(Guid? department, DateTime created, TicketStatus status = TicketStatus.Open, bool assigned = true, DateTime? due = null, double? closedAfterHours = null) =>
        new()
        {
            TicketNumber = "TI-" + Guid.NewGuid().ToString("N")[..10],
            Title = "izvještaj",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = department,
            CreatedAt = created,
            OpenedAtUtc = status == TicketStatus.New ? null : created,
            ClosedAtUtc = closedAfterHours.HasValue ? created.AddHours(closedAfterHours.Value) : null,
            DueDateUtc = due,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            // assigned a long time ago, so that the assignment itself is not what keeps a ticket from being idle
            Assignees = assigned ? new List<TicketAssignee> { new() { UserId = _world.EmpA1.Id, IsPrimary = true, AssignedAtUtc = created } } : new List<TicketAssignee>()
        };

    private async Task SaveAsync(params object[] entities) => await _world.Db(async db => { db.AddRange(entities); return await db.SaveChangesAsync(); });

    private async Task<JsonElement> GetAsync(Actor? who, string path, int expected = 200)
    {
        var response = await _world.Send(who, HttpMethod.Get, path);
        Assert.True(response.Code == expected, $"{path} -> {response.Code}: {response.Text}");
        return string.IsNullOrEmpty(response.Text) ? default : JsonDocument.Parse(response.Text).RootElement.Clone();
    }

    private static List<Guid> Ids(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    // ============================ age ============================

    [SqlFact]
    public async Task Active_tickets_are_grouped_by_age_with_the_oldest_of_each_group_and_a_closed_one_is_not_counted()
    {
        var d = await DepartmentAsync();
        var young = Made(d, Ago(0.5), TicketStatus.New);
        var week1 = Made(d, Ago(5));
        var week2 = Made(d, Ago(6.5), TicketStatus.InProgress);
        var month = Made(d, Ago(20), TicketStatus.Resolved); // resolved is not closed yet
        var old = Made(d, Ago(60));
        var older = Made(d, Ago(90));
        await SaveAsync(young, week1, week2, month, old, older, Made(d, Ago(200), TicketStatus.Closed, closedAfterHours: 1));

        var a = await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}");

        Assert.Equal(6, a.GetProperty("activeTotal").GetInt32());
        var ages = a.GetProperty("ages").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetString()!);
        Assert.Equal(new[] { 1, 2, 1, 2 }, new[] { "upTo2Days", "upTo7Days", "upTo30Days", "older" }.Select(k => ages[k].GetProperty("count").GetInt32()));
        Assert.Equal(week2.Id, ages["upTo7Days"].GetProperty("oldest").GetProperty("id").GetGuid()); // 6.5 days is older than 5
        Assert.Equal(older.Id, ages["older"].GetProperty("oldest").GetProperty("id").GetGuid());
        Assert.Equal(90, ages["older"].GetProperty("oldest").GetProperty("ageDays").GetInt32());
    }

    [SqlFact]
    public async Task A_branch_with_no_active_tickets_has_empty_groups_and_no_oldest()
    {
        var d = await DepartmentAsync();
        await SaveAsync(Made(d, Ago(3), TicketStatus.Closed, closedAfterHours: 1));

        var a = await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}");

        Assert.Equal(0, a.GetProperty("activeTotal").GetInt32());
        Assert.All(a.GetProperty("ages").EnumerateArray(), g =>
        {
            Assert.Equal(0, g.GetProperty("count").GetInt32());
            Assert.Equal(JsonValueKind.Null, g.GetProperty("oldest").ValueKind);
        });
        Assert.Equal(0, a.GetProperty("inactive").GetProperty("total").GetInt32());
    }

    // ============================ standstill ============================

    private TicketMessage Message(Ticket t, MessageType type, double daysAgo) =>
        new() { TicketId = t.Id, AuthorId = _world.EmpA1.Id, Type = type, BodyHtml = "<p>x</p>", CreatedAt = Ago(daysAgo) };

    [SqlFact]
    public async Task A_ticket_is_idle_when_nothing_but_a_system_event_has_happened_for_the_days_chosen()
    {
        var d = await DepartmentAsync();
        var untouched = Made(d, Ago(10));
        var answered = Made(d, Ago(10));
        var noted = Made(d, Ago(10));
        var systemOnly = Made(d, Ago(10));
        var working = Made(d, Ago(10));
        var assignedLately = Made(d, Ago(10));
        assignedLately.Assignees.Single().AssignedAtUtc = Ago(1);
        var audited = Made(d, Ago(10));
        var fresh = Made(d, Ago(1));
        await SaveAsync(untouched, answered, noted, systemOnly, working, assignedLately, audited, fresh);
        await SaveAsync(
            Message(answered, MessageType.Response, 1),
            Message(noted, MessageType.InternalNote, 5), // idle for 5 days: a 3-day threshold sees it, a 7-day one does not
            Message(systemOnly, MessageType.SystemEvent, 1), // not activity of anybody
            new TicketWorkSession { TicketId = working.Id, UserId = _world.EmpA1.Id, StartedAtUtc = Ago(8), EndedAtUtc = null }, // still running
            new AuditLog { Action = "TicketStatusChanged", EntityType = nameof(Ticket), EntityId = audited.Id, Details = "Open -> InProgress", CreatedAt = Ago(1) });

        var three = Ids((await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}&inactiveDays=3")).GetProperty("inactive"));
        var seven = Ids((await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}&inactiveDays=7")).GetProperty("inactive"));
        var two = Ids((await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}&inactiveDays=2")).GetProperty("inactive"));

        Assert.Equal(new[] { untouched.Id, noted.Id, systemOnly.Id }.Order(), three.Order());
        Assert.Equal(new[] { untouched.Id, systemOnly.Id }.Order(), seven.Order());
        Assert.Equal(three.Order(), two.Order()); // nothing in between 2 and 3 days
        Assert.DoesNotContain(fresh.Id, three);
    }

    [SqlFact]
    public async Task The_list_of_idle_tickets_has_the_one_idle_longest_first_and_says_how_many_there_are_in_all()
    {
        var d = await DepartmentAsync();
        var tickets = Enumerable.Range(0, 23).Select(i => Made(d, Ago(30 + i))).ToList(); // 23 idle, ages 30..52 days
        await SaveAsync(tickets.Cast<object>().ToArray());

        var inactive = (await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}")).GetProperty("inactive");

        Assert.Equal(23, inactive.GetProperty("total").GetInt32());
        Assert.Equal(20, Ids(inactive).Count); // as many as are shown
        Assert.Equal(tickets[22].Id, Ids(inactive)[0]);
        Assert.NotEqual(JsonValueKind.Null, inactive.GetProperty("items")[0].GetProperty("lastActivityUtc").ValueKind);
    }

    [SqlFact]
    public async Task Days_without_activity_other_than_2_3_or_7_are_refused()
    {
        var response = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/attention?inactiveDays=4");

        Assert.Equal(400, response.Code);
        Assert.Contains("2, 3 or 7", response.Text);
    }

    [SqlFact]
    public async Task Unassigned_due_soon_and_overdue_are_found_among_the_active_tickets_only()
    {
        var d = await DepartmentAsync();
        var now = DateTime.UtcNow;
        var nobody = Made(d, Ago(2), assigned: false);
        var nobodyClosed = Made(d, Ago(2), TicketStatus.Closed, assigned: false, closedAfterHours: 1);
        var soon = Made(d, Ago(2), due: now.AddHours(10));
        var later = Made(d, Ago(2), due: now.AddHours(30));
        var late = Made(d, Ago(2), due: now.AddHours(-3));
        var lateButClosed = Made(d, Ago(2), TicketStatus.Closed, due: now.AddHours(-3), closedAfterHours: 1);
        await SaveAsync(nobody, nobodyClosed, soon, later, late, lateButClosed);

        var a = await GetAsync(_world.Admin, $"{Base}/attention?departmentId={d}");

        Assert.Equal(new[] { nobody.Id }, Ids(a.GetProperty("unassigned")));
        Assert.Equal(new[] { soon.Id }, Ids(a.GetProperty("dueSoon")));
        Assert.Equal(new[] { late.Id }, Ids(a.GetProperty("overdue")));
        Assert.Equal(1, a.GetProperty("overdue").GetProperty("total").GetInt32());
    }

    // ============================ time to solve ============================

    [SqlFact]
    public async Task The_median_and_the_90th_percentile_show_what_the_average_hides_and_the_average_is_the_one_of_the_overview()
    {
        const int year = 2040;
        var d = await DepartmentAsync();
        var start = new DateTime(year, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        await SaveAsync(new object[] { 1.0, 1.0, 1.0, 1.0, 100.0 }.Select(h => Made(d, start, TicketStatus.Closed, closedAfterHours: (double)h)).ToArray());
        await SaveAsync(Made(d, start, TicketStatus.Open)); // not closed: not in it
        var period = $"from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z&departmentId={d}";

        var r = await GetAsync(_world.Admin, $"{Base}/resolution?{period}");
        var overview = await GetAsync(_world.Admin, $"/api/reports/summary?{period}");

        Assert.Equal(5, r.GetProperty("count").GetInt32());
        Assert.Equal(20.8, r.GetProperty("averageHours").GetDouble());
        Assert.Equal(1.0, r.GetProperty("medianHours").GetDouble());
        Assert.Equal(60.4, r.GetProperty("p90Hours").GetDouble());
        Assert.Equal(overview.GetProperty("avgResolutionHours").GetDouble(), r.GetProperty("averageHours").GetDouble());
    }

    [SqlFact]
    public async Task A_period_with_nothing_closed_has_no_count_and_zeros()
    {
        var d = await DepartmentAsync();

        var r = await GetAsync(_world.Admin, $"{Base}/resolution?from=2039-01-01T00:00:00Z&to=2039-01-31T23:59:59Z&departmentId={d}");

        Assert.Equal(0, r.GetProperty("count").GetInt32());
        Assert.Equal(0, r.GetProperty("p90Hours").GetDouble());
    }

    // ============================ first reply ============================

    [SqlFact]
    public async Task The_first_reply_is_the_first_reply_of_staff_and_not_a_system_event_a_note_or_the_client()
    {
        const int year = 2041;
        var d = await DepartmentAsync();
        var t0 = new DateTime(year, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var one = Made(d, t0);
        var two = Made(d, t0);
        var none = Made(d, t0);
        await SaveAsync(one, two, none);
        TicketMessage Msg(Ticket t, Guid author, MessageType type, double hours) =>
            new() { TicketId = t.Id, AuthorId = author, Type = type, BodyHtml = "<p>x</p>", CreatedAt = t0.AddHours(hours) };
        await SaveAsync(
            Msg(one, _world.EmpA1.Id, MessageType.SystemEvent, 0), // the "ticket opened by" event of a ticket made on behalf: about zero, not an answer
            Msg(one, _world.EmpA1.Id, MessageType.InternalNote, 1),
            Msg(one, _world.CliX.Id, MessageType.Response, 2), // the client writing is not staff answering
            Msg(one, _world.EmpA1.Id, MessageType.Response, 4),
            Msg(one, _world.Admin.Id, MessageType.Response, 6),
            Msg(two, _world.Admin.Id, MessageType.Response, 10),
            Msg(none, _world.EmpA1.Id, MessageType.InternalNote, 3));

        var r = await GetAsync(_world.Admin, $"{Base}/first-response?from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z&departmentId={d}");

        Assert.Equal(3, r.GetProperty("tickets").GetInt32());
        Assert.Equal(2, r.GetProperty("answered").GetInt32());
        Assert.Equal(1, r.GetProperty("unanswered").GetInt32());
        Assert.Equal(7.0, r.GetProperty("averageHours").GetDouble()); // (4 + 10) / 2
        Assert.Equal(7.0, r.GetProperty("medianHours").GetDouble());
        Assert.Equal(9.4, r.GetProperty("p90Hours").GetDouble());
    }

    // ============================ transfers ============================

    private async Task<(Guid a, Guid b, Guid c, Guid fromNone)> TransfersSceneAsync(int year)
    {
        var at = new DateTime(year, 3, 10, 9, 0, 0, DateTimeKind.Utc);
        var other = await DepartmentAsync();
        var third = await DepartmentAsync();
        var ab = Made(_world.BranchA, at);
        var ba = Made(_world.BranchB, at);
        var cc = Made(other, at);
        var none = Made(_world.BranchB, at);
        await SaveAsync(ab, ba, cc, none);
        AuditLog Row(Ticket t, Guid? from, Guid to, double hours) =>
            new() { Action = "TicketTransferred", EntityType = nameof(Ticket), EntityId = t.Id, Details = TicketAuditDetails.Transferred(from, to), CreatedAt = at.AddHours(hours) };
        await SaveAsync(
            Row(ab, _world.BranchA, _world.BranchB, 0),
            Row(ba, _world.BranchB, _world.BranchA, 1),
            Row(cc, other, third, 2), // between two other branches: not the business of A
            Row(none, null, _world.BranchB, 3)); // unrouted to B
        return (ab.Id, ba.Id, cc.Id, none.Id);
    }

    [SqlFact]
    public async Task Staff_see_the_transfers_that_left_or_entered_their_branch_and_Admin_sees_all_or_those_of_one_branch()
    {
        const int year = 2042;
        var (ab, ba, cc, none) = await TransfersSceneAsync(year);
        var period = $"from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z";

        var staff = await GetAsync(_world.EmpA1, $"{Base}/transfers?{period}");
        var staffAsksForB = await GetAsync(_world.EmpA1, $"{Base}/transfers?{period}&departmentId={_world.BranchB}"); // what staff ask for does not widen it
        var admin = await GetAsync(_world.Admin, $"{Base}/transfers?{period}");
        var adminB = await GetAsync(_world.Admin, $"{Base}/transfers?{period}&departmentId={_world.BranchB}");
        var adminC = await GetAsync(_world.Admin, $"{Base}/transfers?{period}&departmentId={Guid.NewGuid()}");

        List<Guid> Tickets(JsonElement x) => x.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("ticketId").GetGuid()).ToList();
        Assert.Equal(new[] { ab, ba }.Order(), Tickets(staff).Order());
        Assert.Equal(new[] { ab, ba }.Order(), Tickets(staffAsksForB).Order());
        Assert.Equal(new[] { ab, ba, cc, none }.Order(), Tickets(admin).Order());
        Assert.Equal(4, admin.GetProperty("total").GetInt32());
        Assert.Equal(new[] { ab, ba, none }.Order(), Tickets(adminB).Order());
        Assert.Equal(0, adminC.GetProperty("total").GetInt32());
    }

    [SqlFact]
    public async Task The_transfers_are_counted_from_a_branch_to_a_branch_and_an_unrouted_ticket_has_no_from()
    {
        const int year = 2043;
        await TransfersSceneAsync(year);

        var admin = await GetAsync(_world.Admin, $"{Base}/transfers?from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z");

        var edges = admin.GetProperty("edges").EnumerateArray().ToList();
        Assert.Equal(4, edges.Count);
        Assert.All(edges, e => Assert.Equal(1, e.GetProperty("count").GetInt32()));
        var unrouted = Assert.Single(edges, e => e.GetProperty("fromId").ValueKind == JsonValueKind.Null);
        Assert.Equal(JsonValueKind.Null, unrouted.GetProperty("fromName").ValueKind);
        Assert.Equal("Branch B", unrouted.GetProperty("toName").GetString());
    }

    [SqlFact]
    public async Task The_period_of_the_transfers_is_respected()
    {
        const int year = 2044;
        await TransfersSceneAsync(year);

        var outside = await GetAsync(_world.Admin, $"{Base}/transfers?from={year}-04-01T00:00:00Z&to={year}-04-30T23:59:59Z");

        Assert.Equal(0, outside.GetProperty("total").GetInt32());
    }

    // ============================ the isolation of branches ============================

    [SqlFact]
    public async Task Staff_do_not_see_the_tickets_of_another_branch_in_the_lists_and_the_figures()
    {
        var b = Made(_world.BranchB, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), assigned: false, due: new DateTime(2001, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await SaveAsync(b);

        var theirs = await GetAsync(_world.EmpB1, $"{Base}/attention");
        var other = await GetAsync(_world.EmpA1, $"{Base}/attention?departmentId={_world.BranchB}"); // the filter does not widen it for staff
        var admin = await GetAsync(_world.Admin, $"{Base}/attention?departmentId={_world.BranchB}");

        foreach (var field in new[] { "unassigned", "overdue", "inactive" })
        {
            Assert.Contains(b.Id, Ids(theirs.GetProperty(field)));
            Assert.DoesNotContain(b.Id, Ids(other.GetProperty(field)));
            Assert.Contains(b.Id, Ids(admin.GetProperty(field)));
        }
        Assert.Equal(b.Id, theirs.GetProperty("ages").EnumerateArray().Single(g => g.GetProperty("key").GetString() == "older").GetProperty("oldest").GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task Staff_get_the_times_of_their_own_branch_only()
    {
        const int year = 2045;
        var start = new DateTime(year, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var closedInB = Made(_world.BranchB, start, TicketStatus.Closed, closedAfterHours: 5);
        await SaveAsync(closedInB);
        var period = $"from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z";

        var b = await GetAsync(_world.EmpB1, $"{Base}/resolution?{period}");
        var a = await GetAsync(_world.EmpA1, $"{Base}/resolution?{period}&departmentId={_world.BranchB}");

        Assert.Equal(1, b.GetProperty("count").GetInt32());
        Assert.Equal(5.0, b.GetProperty("medianHours").GetDouble());
        Assert.Equal(0, a.GetProperty("count").GetInt32());
    }

    [SqlFact]
    public async Task A_client_may_not_read_any_of_them()
    {
        foreach (var path in new[] { "attention", "resolution", "first-response", "transfers" })
            Assert.Equal(403, (await _world.Send(_world.CliX, HttpMethod.Get, $"{Base}/{path}")).Code);
    }

    [SqlFact]
    public async Task A_reversed_period_is_refused()
    {
        Assert.Equal(400, (await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/resolution?from=2040-05-01T00:00:00Z&to=2040-04-01T00:00:00Z")).Code);
    }
}
