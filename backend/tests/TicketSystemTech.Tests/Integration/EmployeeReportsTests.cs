using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The reports about people: the load now, the recorded hours, what a person is good at, who is the only one who knows a company, and the monthly worksheet.
/// A test that counts by hand records its work in a year of its own (EmpA1 is the person), so that nothing else can be in the figures.
/// </summary>
[Collection(ApiCollection.Name)]
public class EmployeeReportsTests : IAsyncLifetime
{
    private const string Base = "/api/reports/employees";

    private readonly ApiWorld _world;
    private readonly List<Guid> _companies = new();
    private readonly List<Guid> _lookups = new();

    public EmployeeReportsTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => t.TicketNumber.StartsWith("ER-")).ExecuteDeleteAsync(); // sessions and assignees go with their tickets
            await db.WorkTasks.Where(t => t.Title.StartsWith("ER ")).ExecuteDeleteAsync();
            await db.Companies.Where(c => _companies.Contains(c.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
            return await db.TicketCategories.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
        });
    }

    private static DateTime At(int y, int m, int d, int h = 9, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private Ticket Made(DateTime created, TicketStatus status = TicketStatus.Open, Guid? company = null, Actor? closedBy = null, DateTime? closedAt = null,
        Guid? topic = null, Guid? category = null, DateTime? due = null, params (Actor Who, bool Primary)[] on)
    {
        var t = new Ticket
        {
            TicketNumber = "ER-" + Guid.NewGuid().ToString("N")[..10],
            Title = "ljudi",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = company ?? _world.CompanyId,
            Status = status,
            DepartmentId = _world.BranchA,
            CreatedAt = created,
            OpenedAtUtc = created,
            HelpTopicId = topic,
            CategoryId = category,
            DueDateUtc = due,
            ClosedAtUtc = status == TicketStatus.Closed ? closedAt ?? created.AddHours(2) : null,
            ClosedByUserId = status == TicketStatus.Closed ? closedBy?.Id : null,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        foreach (var (who, primary) in on) t.Assignees.Add(new TicketAssignee { UserId = who.Id, IsPrimary = primary, AssignedAtUtc = created });
        return t;
    }

    private async Task SaveAsync(params object[] entities) => await _world.Db(async db => { db.AddRange(entities); return await db.SaveChangesAsync(); });

    private static TicketWorkSession Session(Ticket t, Actor who, DateTime start, double seconds) =>
        new() { TicketId = t.Id, UserId = who.Id, StartedAtUtc = start, EndedAtUtc = start.AddSeconds(seconds) };

    private WorkTask Task(Actor to, WorkTaskStatus status = WorkTaskStatus.Pending) =>
        new() { Title = "ER " + Guid.NewGuid().ToString("N")[..6], Description = "x", DepartmentId = _world.BranchA, CreatedByUserId = _world.Admin.Id, AssignedByUserId = _world.Admin.Id, AssignedToUserId = to.Id, Status = status };

    private static WorkTaskSession TaskSession(WorkTask t, Actor who, DateTime start, double seconds) =>
        new() { WorkTaskId = t.Id, UserId = who.Id, StartedAtUtc = start, EndedAtUtc = start.AddSeconds(seconds) };

    private async Task<JsonElement> GetAsync(Actor? who, string path, int expected = 200)
    {
        var response = await _world.Send(who, HttpMethod.Get, path);
        Assert.True(response.Code == expected, $"{path} -> {response.Code}: {response.Text}");
        return string.IsNullOrEmpty(response.Text) ? default : JsonDocument.Parse(response.Text).RootElement.Clone();
    }

    private static string Period(int year, int month = 3) => $"from={year}-{month:00}-01T00:00:00Z&to={year}-{month:00}-28T20:00:00Z"; // the 1st to the 28th, local time

    private static JsonElement Person(JsonElement list, Actor who) => list.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("userId").GetGuid() == who.Id);

    // ============================ the load ============================

    [SqlFact]
    public async Task The_load_counts_active_tickets_as_main_or_help_the_overdue_ones_and_the_tasks_that_are_not_done()
    {
        var before = Person(await GetAsync(_world.Admin, $"{Base}/workload"), _world.EmpA1);
        var now = DateTime.UtcNow;
        var main1 = Made(now.AddDays(-5), due: now.AddDays(-1), on: (_world.EmpA1, true));
        var main2 = Made(now.AddDays(-3), TicketStatus.Resolved, on: (_world.EmpA1, true)); // resolved is not closed yet
        var help = Made(now.AddDays(-2), TicketStatus.InProgress, due: now.AddDays(-2), on: new[] { (_world.EmpA2, true), (_world.EmpA1, false) });
        var closed = Made(now.AddDays(-9), TicketStatus.Closed, closedBy: _world.EmpA1, on: (_world.EmpA1, true)); // closed: not on the plate
        var overdueClosed = Made(now.AddDays(-9), TicketStatus.Closed, due: now.AddDays(-8), on: (_world.EmpA1, true));
        await SaveAsync(main1, main2, help, closed, overdueClosed);
        await SaveAsync(Task(_world.EmpA1), Task(_world.EmpA1, WorkTaskStatus.InProgress), Task(_world.EmpA1, WorkTaskStatus.Done));

        var after = Person(await GetAsync(_world.Admin, $"{Base}/workload"), _world.EmpA1);

        int Delta(string field) => after.GetProperty(field).GetInt32() - before.GetProperty(field).GetInt32();
        Assert.Equal(3, Delta("openTickets"));
        Assert.Equal(2, Delta("mainTickets"));
        Assert.Equal(2, Delta("overdueTickets")); // main1 and help, not the closed one
        Assert.Equal(2, Delta("openTasks")); // the finished one is not open
    }

    [SqlFact]
    public async Task An_employee_gets_only_their_own_load_and_Admin_may_narrow_to_a_branch()
    {
        var own = await GetAsync(_world.EmpA1, $"{Base}/workload");
        var narrowed = await GetAsync(_world.EmpA1, $"{Base}/workload?departmentId={_world.BranchB}"); // does not widen it
        var admin = await GetAsync(_world.Admin, $"{Base}/workload");
        var branchB = await GetAsync(_world.Admin, $"{Base}/workload?departmentId={_world.BranchB}");

        Assert.Equal(new[] { _world.EmpA1.Id }, own.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("userId").GetGuid()));
        Assert.Equal(new[] { _world.EmpA1.Id }, narrowed.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("userId").GetGuid()));
        var everybody = admin.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("userId").GetGuid()).ToList();
        Assert.Contains(_world.EmpA1.Id, everybody);
        Assert.Contains(_world.EmpB1.Id, everybody);
        var inB = branchB.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("userId").GetGuid()).ToList();
        Assert.Contains(_world.EmpB1.Id, inB);
        Assert.DoesNotContain(_world.EmpA1.Id, inB);
    }

    // ============================ the hours ============================

    [SqlFact]
    public async Task Hours_are_added_up_from_the_sessions_by_day_and_week_tickets_apart_from_tasks_and_rounded_at_the_end()
    {
        const int year = 2046;
        var t1 = Made(At(year, 3, 1));
        var t2 = Made(At(year, 3, 1));
        await SaveAsync(t1, t2);
        var task = Task(_world.EmpA1);
        await SaveAsync(task);
        await SaveAsync(
            Session(t1, _world.EmpA1, At(year, 3, 10), 1500), Session(t2, _world.EmpA1, At(year, 3, 10, 11), 1500), // Wednesday: 2 × 25 min of tickets
            Session(t1, _world.EmpA1, At(year, 3, 11), 1500), // Thursday: 25 min
            Session(t1, _world.EmpA2, At(year, 3, 10), 36000), // somebody else's hours are not these
            TaskSession(task, _world.EmpA1, At(year, 3, 10, 14), 3600)); // Wednesday, a task

        var detail = await GetAsync(_world.Admin, $"{Base}/hours/{_world.EmpA1.Id}?{Period(year)}");

        Assert.Equal(1.25, detail.GetProperty("ticketHours").GetDouble()); // 4500 s: 1.25 h, and not 3 × 0.42
        Assert.Equal(1.0, detail.GetProperty("taskHours").GetDouble());
        Assert.Equal(2.25, detail.GetProperty("totalHours").GetDouble());
        var days = detail.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(28, days.Count); // every day of the period has a line
        var wednesday = days.Single(d => d.GetProperty("day").GetString() == $"{year}-03-10");
        Assert.Equal(0.83, wednesday.GetProperty("ticketHours").GetDouble()); // 3000 s
        Assert.Equal(1.0, wednesday.GetProperty("taskHours").GetDouble());
        Assert.Equal(1.83, wednesday.GetProperty("totalHours").GetDouble());
        var weeks = detail.GetProperty("weeks").EnumerateArray().ToList();
        Assert.All(weeks, w => Assert.Equal(DayOfWeek.Monday, DateOnly.Parse(w.GetProperty("weekStart").GetString()!).DayOfWeek));
        Assert.Equal(2.25, weeks.Sum(w => w.GetProperty("totalHours").GetDouble()));

        var everybody = await GetAsync(_world.Admin, $"{Base}/hours?{Period(year)}");
        Assert.Equal(2.25, Person(everybody, _world.EmpA1).GetProperty("totalHours").GetDouble());
        Assert.Equal(10.0, Person(everybody, _world.EmpA2).GetProperty("totalHours").GetDouble());
    }

    [SqlFact]
    public async Task A_session_that_is_still_running_is_counted_up_to_now()
    {
        var t = Made(DateTime.UtcNow.AddDays(-1));
        await SaveAsync(t);
        await SaveAsync(new TicketWorkSession { TicketId = t.Id, UserId = _world.EmpA1.Id, StartedAtUtc = DateTime.UtcNow.AddHours(-2), EndedAtUtc = null });

        var detail = await GetAsync(_world.Admin, $"{Base}/hours/{_world.EmpA1.Id}?from={DateTime.UtcNow.AddDays(-1):yyyy-MM-ddTHH:mm:ssZ}&to={DateTime.UtcNow.AddHours(1):yyyy-MM-ddTHH:mm:ssZ}");

        var hours = detail.GetProperty("ticketHours").GetDouble();
        Assert.InRange(hours, 1.99, 2.1);
    }

    [SqlFact]
    public async Task The_working_days_with_nothing_recorded_are_found_only_after_the_account_was_made_and_a_weekend_is_not_one()
    {
        const int year = 2024; // March 2024: 21 working days; the 4th is a Monday and the 9th a Saturday
        var t = Made(At(year, 3, 1));
        await SaveAsync(t);
        await SaveAsync(Session(t, _world.EmpA1, At(year, 3, 4), 600), Session(t, _world.EmpA1, At(year, 3, 9), 600));
        await _world.Db(db => db.Users.Where(u => u.Id == _world.EmpA1.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAtUtc, At(2020, 1, 1))));
        try
        {
            var detail = await GetAsync(_world.Admin, $"{Base}/hours/{_world.EmpA1.Id}?{Period(year)}");
            var without = detail.GetProperty("daysWithoutEntry").EnumerateArray().Select(d => DateOnly.Parse(d.GetString()!)).ToList();

            Assert.True(detail.GetProperty("daysWithoutEntryApply").GetBoolean());
            Assert.DoesNotContain(without, d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
            Assert.DoesNotContain(new DateOnly(year, 3, 4), without);
            Assert.Equal(19, without.Count); // 20 working days in the first 28 days, minus the Monday with work

            var people = await GetAsync(_world.Admin, $"{Base}/hours?{Period(year)}");
            Assert.Equal(19, Person(people, _world.EmpA1).GetProperty("daysWithoutEntry").GetInt32());

            // an account made after the period has no days without entry in it
            await _world.Db(db => db.Users.Where(u => u.Id == _world.EmpA1.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAtUtc, At(year, 3, 20))));
            var later = await GetAsync(_world.Admin, $"{Base}/hours/{_world.EmpA1.Id}?{Period(year)}");
            Assert.All(later.GetProperty("daysWithoutEntry").EnumerateArray(), d => Assert.True(DateOnly.Parse(d.GetString()!) >= new DateOnly(year, 3, 20)));
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.EmpA1.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAtUtc, DateTime.UtcNow)));
        }
    }

    [SqlFact]
    public async Task An_employee_sees_only_their_own_hours_and_is_refused_those_of_another_person()
    {
        const int year = 2046;
        var own = await GetAsync(_world.EmpA1, $"{Base}/hours?{Period(year)}");
        var mine = await GetAsync(_world.EmpA1, $"{Base}/hours/{_world.EmpA1.Id}?{Period(year)}");

        Assert.Equal(new[] { _world.EmpA1.Id }, own.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("userId").GetGuid()));
        Assert.Equal(_world.EmpA1.Id, mine.GetProperty("userId").GetGuid());
        await GetAsync(_world.EmpA1, $"{Base}/hours/{_world.EmpA2.Id}?{Period(year)}", 403); // a colleague of the same branch
        await GetAsync(_world.EmpA1, $"{Base}/hours/{_world.EmpB1.Id}?{Period(year)}", 403);
        await GetAsync(_world.EmpA1, $"{Base}/hours/{_world.Admin.Id}?{Period(year)}", 403);
        await GetAsync(_world.Admin, $"{Base}/hours/{Guid.NewGuid()}?{Period(year)}", 404);
        Assert.Equal(400, (await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/hours?from=2046-05-01T00:00:00Z&to=2046-04-01T00:00:00Z")).Code);
    }

    // ============================ what a person is good at ============================

    [SqlFact]
    public async Task The_top_three_of_a_person_are_ranked_by_the_tickets_they_closed_and_then_by_the_hours()
    {
        const int year = 2047;
        var topics = Enumerable.Range(1, 4).Select(i => new HelpTopic { Name = $"ER tema {i}" }).ToList();
        var category = new TicketCategory { Name = "ER kategorija" };
        await SaveAsync(topics.Cast<object>().Append(category).ToArray());
        _lookups.AddRange(topics.Select(t => t.Id).Append(category.Id));
        var big = new Company { Name = "ER Velika" };
        var small = new Company { Name = "ER Mala" };
        await SaveAsync(big, small);
        _companies.AddRange(new[] { big.Id, small.Id });

        var closed = At(year, 3, 10);
        var tickets = new List<Ticket>
        {
            Made(closed, TicketStatus.Closed, big.Id, _world.EmpA1, closed, topics[0].Id, category.Id), Made(closed, TicketStatus.Closed, big.Id, _world.EmpA1, closed, topics[0].Id, category.Id),
            Made(closed, TicketStatus.Closed, big.Id, _world.EmpA1, closed, topics[0].Id, null), Made(closed, TicketStatus.Closed, small.Id, _world.EmpA1, closed, topics[1].Id, null),
            Made(closed, TicketStatus.Closed, small.Id, _world.EmpA1, closed, topics[2].Id, null), Made(closed, TicketStatus.Closed, small.Id, _world.EmpA1, closed, topics[3].Id, null),
            Made(closed, TicketStatus.Closed, small.Id, _world.EmpA2, closed, topics[3].Id, null), // closed by somebody else: not theirs
            Made(closed, TicketStatus.Closed, small.Id, _world.EmpA1, At(year + 1, 1, 1), topics[3].Id, null) // closed after the period
        };
        await SaveAsync(tickets.Cast<object>().ToArray());
        await SaveAsync(
            Session(tickets[3], _world.EmpA1, At(year, 3, 11), 7200), // topic 2: 2 h, closes 1
            Session(tickets[4], _world.EmpA1, At(year, 3, 11), 3600), // topic 3: 1 h, closes 1
            Session(tickets[5], _world.EmpA1, At(year, 3, 11), 1800)); // topic 4: 0.5 h, closes 1

        var s = await GetAsync(_world.Admin, $"{Base}/specialization?userId={_world.EmpA1.Id}&{Period(year)}");

        Assert.Equal(6, s.GetProperty("closedTotal").GetInt32());
        var top = s.GetProperty("topics").EnumerateArray().ToList();
        Assert.Equal(3, top.Count); // only the first three of four
        Assert.Equal(new[] { "ER tema 1", "ER tema 2", "ER tema 3" }, top.Select(t => t.GetProperty("name").GetString())); // 3 closed, then the two with 1 closed by their hours
        Assert.Equal(3, top[0].GetProperty("closed").GetInt32());
        Assert.Equal(2.0, top[1].GetProperty("hours").GetDouble());
        Assert.Equal("ER kategorija", Assert.Single(s.GetProperty("categories").EnumerateArray()).GetProperty("name").GetString()); // a ticket with no category adds none
        var companies = s.GetProperty("companies").EnumerateArray().Select(c => (c.GetProperty("name").GetString()!, c.GetProperty("closed").GetInt32())).ToList();
        Assert.Equal(new[] { ("ER Velika", 3), ("ER Mala", 3) }.OrderBy(x => x.Item1), companies.OrderBy(x => x.Item1));
    }

    [SqlFact]
    public async Task Admin_must_say_whom_and_an_employee_gets_themself_and_nobody_else()
    {
        const int year = 2047;
        var asAdmin = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/specialization?{Period(year)}");
        var self = await GetAsync(_world.EmpA1, $"{Base}/specialization?{Period(year)}");
        var other = await GetAsync(_world.EmpA1, $"{Base}/specialization?userId={_world.EmpA2.Id}&{Period(year)}", 403);

        Assert.Equal(400, asAdmin.Code);
        Assert.Contains("Choose a person first.", asAdmin.Text);
        Assert.Equal(_world.EmpA1.Id, self.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Undefined, other.ValueKind);
    }

    // ============================ the dependence on one person ============================

    [SqlFact]
    public async Task A_company_that_one_person_closed_most_of_is_listed_with_the_share_and_a_high_mark_from_80_percent()
    {
        var a = new Company { Name = "ER Zavisna" };
        var b = new Company { Name = "ER Podijeljena" };
        var c = new Company { Name = "ER Malo" };
        var d = new Company { Name = "ER Staro" };
        await SaveAsync(a, b, c, d);
        _companies.AddRange(new[] { a.Id, b.Id, c.Id, d.Id });
        var now = DateTime.UtcNow;
        Ticket Closed(Company company, Actor by, double daysAgo) => Made(now.AddDays(-daysAgo - 1), TicketStatus.Closed, company.Id, by, now.AddDays(-daysAgo));
        var tickets = new List<Ticket>();
        tickets.AddRange(Enumerable.Range(0, 5).Select(_ => Closed(a, _world.EmpA1, 10)));
        tickets.Add(Closed(a, _world.EmpA2, 10)); // 5 of 6 = 83.3 %: high
        tickets.AddRange(Enumerable.Range(0, 3).Select(_ => Closed(b, _world.EmpA1, 20)));
        tickets.AddRange(Enumerable.Range(0, 3).Select(_ => Closed(b, _world.EmpA2, 20))); // 3 of 6 = 50 %: listed, not high
        tickets.AddRange(Enumerable.Range(0, 4).Select(_ => Closed(c, _world.EmpA1, 10))); // all of them, but only 4 tickets: under the minimum
        tickets.AddRange(Enumerable.Range(0, 6).Select(_ => Closed(d, _world.EmpA1, 400))); // all of them, but more than a year ago
        await SaveAsync(tickets.Cast<object>().ToArray());

        var r = await GetAsync(_world.Admin, $"{Base}/dependency");

        var items = r.GetProperty("items").EnumerateArray().Where(i => new[] { a.Id, b.Id, c.Id, d.Id }.Contains(i.GetProperty("companyId").GetGuid())).ToList();
        Assert.Equal(new[] { "ER Zavisna", "ER Podijeljena" }, items.Select(i => i.GetProperty("company").GetString()));
        Assert.Equal(83.3, items[0].GetProperty("sharePercent").GetDouble());
        Assert.True(items[0].GetProperty("high").GetBoolean());
        Assert.Equal(6, items[0].GetProperty("closed").GetInt32());
        Assert.Equal(5, items[0].GetProperty("personClosed").GetInt32());
        Assert.Equal(_world.EmpA1.Id, items[0].GetProperty("personId").GetGuid());
        Assert.Equal(50.0, items[1].GetProperty("sharePercent").GetDouble());
        Assert.False(items[1].GetProperty("high").GetBoolean());

        var stricter = await GetAsync(_world.Admin, $"{Base}/dependency?minShare=80&minClosed=6");
        Assert.Contains(stricter.GetProperty("items").EnumerateArray(), i => i.GetProperty("companyId").GetGuid() == a.Id);
        Assert.DoesNotContain(stricter.GetProperty("items").EnumerateArray(), i => i.GetProperty("companyId").GetGuid() == b.Id);
    }

    [SqlFact]
    public async Task Only_Admin_may_see_which_company_one_person_alone_knows()
    {
        await GetAsync(_world.EmpA1, $"{Base}/dependency", 403);
        await GetAsync(_world.CliX, $"{Base}/dependency", 403);
        await GetAsync(null, $"{Base}/dependency", 401);
    }

    // ============================ the worksheet ============================

    private async Task<XSSFWorkbook> SheetAsync(Actor who, string query, int expected = 200)
    {
        var file = await _world.GetBytes(who, $"{Base}/timesheet?{query}");
        Assert.True(file.Code == expected, $"{query} -> {file.Code}");
        return expected == 200 ? new XSSFWorkbook(new MemoryStream(file.Bytes)) : null!;
    }

    [SqlFact]
    public async Task The_worksheet_holds_only_recorded_sessions_of_that_month_in_the_business_zone_with_the_totals()
    {
        const int year = 2048;
        var ticket = Made(At(year, 2, 1));
        await SaveAsync(ticket);
        var task = Task(_world.EmpA1);
        await SaveAsync(task);
        await SaveAsync(
            Session(ticket, _world.EmpA1, At(year, 3, 10), 7200), // 2 h
            Session(ticket, _world.EmpA1, At(year, 2, 29, 23, 30), 1800), // 00:30 on 1 March in Sarajevo: March, although it is February in UTC
            Session(ticket, _world.EmpA1, At(year, 3, 31, 22, 30), 3600), // 00:30 on 1 April there: not March
            Session(ticket, _world.EmpA2, At(year, 3, 10), 9999), // somebody else's
            TaskSession(task, _world.EmpA1, At(year, 3, 12), 3600));

        using var wb = await SheetAsync(_world.Admin, $"userId={_world.EmpA1.Id}&year={year}&month=3&language=en");

        var days = wb.GetSheet("Overview by days");
        var total = days.GetRow(days.LastRowNum);
        Assert.Equal("Total", total.GetCell(0).StringCellValue);
        Assert.Equal(2.5, total.GetCell(1).NumericCellValue); // tickets: 7200 + 1800 s
        Assert.Equal(1.0, total.GetCell(2).NumericCellValue);
        Assert.Equal(3.5, total.GetCell(3).NumericCellValue);
        Assert.Equal(39, days.LastRowNum); // the header block, the heading, 31 days of March and the total
        var companies = wb.GetSheet("By company");
        Assert.Contains(Enumerable.Range(8, companies.LastRowNum - 7).Select(i => companies.GetRow(i).GetCell(0).StringCellValue), v => v == "Tasks (no company)");
        var raw = wb.GetSheet("Raw list");
        Assert.Equal(3, raw.LastRowNum - 7); // three sessions of that month: the other two are another month or another person
        Assert.Contains("01.03.2048 00:30", raw.GetRow(8).GetCell(0).StringCellValue);
    }

    [SqlFact]
    public async Task The_worksheet_is_in_the_language_asked_for_or_else_the_language_of_the_person_who_asks()
    {
        using var asked = await SheetAsync(_world.Admin, $"userId={_world.EmpA1.Id}&year=2048&month=3&language=bs");
        using var theirOwn = await SheetAsync(_world.Admin, $"userId={_world.EmpA1.Id}&year=2048&month=3");
        await _world.Db(db => db.Users.Where(u => u.Id == _world.Admin.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.PreferredLanguage, "bs")));
        try
        {
            using var bosnian = await SheetAsync(_world.Admin, $"userId={_world.EmpA1.Id}&year=2048&month=3");
            Assert.Equal("Pregled po danima", bosnian.GetSheetName(0));
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.Admin.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.PreferredLanguage, "en")));
        }

        Assert.Equal("Pregled po danima", asked.GetSheetName(0));
        Assert.Equal("Overview by days", theirOwn.GetSheetName(0));
    }

    [SqlFact]
    public async Task An_employee_may_download_only_their_own_worksheet_and_a_client_none()
    {
        using (await SheetAsync(_world.EmpA1, $"userId={_world.EmpA1.Id}&year=2048&month=3")) { }
        await SheetAsync(_world.EmpA1, $"userId={_world.EmpA2.Id}&year=2048&month=3", 403);
        await SheetAsync(_world.EmpA1, $"userId={_world.Admin.Id}&year=2048&month=3", 403);
        await SheetAsync(_world.CliX, $"userId={_world.CliX.Id}&year=2048&month=3", 403);
        await SheetAsync(_world.Admin, $"userId={Guid.NewGuid()}&year=2048&month=3", 404);
        var bad = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/timesheet?userId={_world.EmpA1.Id}&year=2048&month=13");
        Assert.Equal(400, bad.Code);
        Assert.Contains("The month is not valid.", bad.Text);
    }

    [SqlFact]
    public async Task The_worksheet_is_offered_under_a_name_that_says_whose_and_which_month()
    {
        var file = await _world.GetBytes(_world.Admin, $"{Base}/timesheet?userId={_world.EmpA1.Id}&year=2048&month=3");

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Matches(@"^timesheet-[a-z0-9-]+-2048-03\.xlsx$", file.FileName!);
    }
}
