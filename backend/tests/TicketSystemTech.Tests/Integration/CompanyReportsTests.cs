using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The statistics of the companies (the second tab of the overview). Their figures are for all branches together, for the staff of every branch alike, while the old
/// reports and the list of tickets stay limited to the branch of whoever asks: both are tested here. The data is made for each test in a year of its own, in companies
/// of its own, so that the figures can be counted by hand and nothing else in the database can change them.
/// </summary>
[Collection(ApiCollection.Name)]
public class CompanyReportsTests : IAsyncLifetime
{
    private const string Mark = "RP ";
    private const string Base = "/api/reports/companies";

    private readonly ApiWorld _world;
    private HelpTopic _t1 = null!, _t2 = null!;
    private TicketCategory _k1 = null!;
    private SubBranch _sub = null!;

    public CompanyReportsTests(ApiWorld world) => _world = world;

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        _t1 = new HelpTopic { Name = Mark + "mreža" };
        _t2 = new HelpTopic { Name = Mark + "štampač" };
        _k1 = new TicketCategory { Name = Mark + "hardver" };
        _sub = new SubBranch { Name = Mark + "podposlovnica", DepartmentId = _world.BranchA };
        await _world.Db(async db => { db.AddRange(_t1, _t2, _k1, _sub); return await db.SaveChangesAsync(); });
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => t.TicketNumber.StartsWith("RP-")).ExecuteDeleteAsync();
            await db.Companies.Where(c => c.Name.StartsWith(Mark)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => h.Name.StartsWith(Mark)).ExecuteDeleteAsync();
            await db.TicketCategories.Where(h => h.Name.StartsWith(Mark)).ExecuteDeleteAsync();
            return await db.SubBranches.Where(s => s.Name.StartsWith(Mark)).ExecuteDeleteAsync();
        });
    }

    // ============================ the data, counted by hand ============================

    private sealed record Scene(int Year, Company A, Company B, Company C, string Query)
    {
        public string Period => $"from={Year}-03-01T00:00:00Z&to={Year}-03-31T23:59:59Z";
    }

    private static DateTime At(int year, int month, int day, int hour = 9) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private Ticket Made(Company company, Actor client, DateTime created, TicketStatus status, Guid? branch, HelpTopic? topic = null, TicketCategory? category = null,
        double? openedAfterHours = null, double? closedAfterHours = null, double? dueAfterHours = null, Actor? closedBy = null, SubBranch? sub = null) =>
        new()
        {
            TicketNumber = "RP-" + Guid.NewGuid().ToString("N")[..10],
            Title = "izvještaj",
            Description = "<p>x</p>",
            ClientId = client.Id,
            CompanyId = company.Id,
            Status = status,
            DepartmentId = branch,
            SubBranchId = sub?.Id,
            HelpTopicId = topic?.Id,
            CategoryId = category?.Id,
            CreatedAt = created,
            OpenedAtUtc = openedAfterHours.HasValue ? created.AddHours(openedAfterHours.Value) : null,
            ClosedAtUtc = closedAfterHours.HasValue ? created.AddHours(closedAfterHours.Value) : null,
            ClosedByUserId = closedBy?.Id,
            DueDateUtc = dueAfterHours.HasValue ? created.AddHours(dueAfterHours.Value) : null,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };

    /// <summary>
    /// March of the year is the period. In it: A has five tickets (two closed, one on time and one late; two in branch B; three of them by X, two by Y), B has two, C has one.
    /// Before it, in February: A two, B four. One of A's February tickets was closed in March, by EmpA1.
    /// </summary>
    private async Task<Scene> SceneAsync(int year)
    {
        var a = new Company { Name = Mark + "Alfa " + year };
        var b = new Company { Name = Mark + "Beta " + year };
        var c = new Company { Name = Mark + "Gama " + year };
        await _world.Db(async db => { db.AddRange(a, b, c); return await db.SaveChangesAsync(); });

        var X = _world.CliX;
        var Y = _world.CliY;
        var A1 = _world.EmpA1;
        var B1 = _world.EmpB1;
        var tickets = new List<Ticket>
        {
            // A, in the period
            Made(a, X, At(year, 3, 3), TicketStatus.Closed, _world.BranchA, _t1, _k1, 1, 5, 10, A1, _sub),   // on time, 4 h
            Made(a, X, At(year, 3, 3), TicketStatus.Closed, _world.BranchA, _t1, null, 1, 49, 24, B1),       // late, 48 h; closed by a man of branch B
            Made(a, X, At(year, 3, 10), TicketStatus.Open, _world.BranchB, _t2, _k1),
            Made(a, Y, At(year, 3, 10), TicketStatus.New, _world.BranchB),                                    // no topic, no category yet
            Made(a, Y, At(year, 3, 20), TicketStatus.InProgress, _world.BranchA),
            // B and C, in the period
            Made(b, X, At(year, 3, 12), TicketStatus.Open, _world.BranchA, _t2, _k1),
            Made(b, X, At(year, 3, 12), TicketStatus.New, _world.BranchA, _t2, _k1),
            Made(c, Y, At(year, 3, 15), TicketStatus.Closed, _world.BranchB, _t1, null, 1, 4, null, A1),      // closed, no due date
            // before the period (the one of A that is closed in the period was made in February and closed on the fifth of March by EmpA1)
            Made(a, X, At(year, 2, 20), TicketStatus.Closed, _world.BranchB, _t2, null, 1, 24 * 13, 24 * 20, A1),
            Made(a, Y, At(year, 2, 10), TicketStatus.Open, _world.BranchA),
        };
        for (var i = 0; i < 4; i++) tickets.Add(Made(b, X, At(year, 2, 10), TicketStatus.Open, _world.BranchA, _t2));
        await _world.Db(async db => { db.Tickets.AddRange(tickets); return await db.SaveChangesAsync(); });
        return new Scene(year, a, b, c, "");
    }

    private async Task<JsonElement> GetAsync(Actor? who, string path, int expected = 200)
    {
        var response = await _world.Send(who, HttpMethod.Get, path);
        Assert.True(response.Code == expected, $"{path} -> {response.Code}: {response.Text}");
        return string.IsNullOrEmpty(response.Text) ? default : JsonDocument.Parse(response.Text).RootElement.Clone();
    }

    private static JsonElement Of(JsonElement items, string name) => items.EnumerateArray().Single(i => i.GetProperty("name").GetString() == name);

    // ============================ the ranking ============================

    [SqlFact]
    public async Task The_top_has_the_companies_with_the_most_tickets_created_in_the_period_with_the_figures_counted_by_hand()
    {
        var s = await SceneAsync(2031);

        var top = await GetAsync(_world.Admin, $"{Base}/top?{s.Period}");

        Assert.Equal(8, top.GetProperty("totalTickets").GetInt32());
        Assert.Equal(8, top.GetProperty("topTickets").GetInt32());
        Assert.Equal(3, top.GetProperty("totalCompanies").GetInt32());
        var items = top.GetProperty("items");
        Assert.Equal(new[] { s.A.Name, s.B.Name, s.C.Name }, items.EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray());

        var a = Of(items, s.A.Name);
        Assert.Equal(5, a.GetProperty("total").GetInt32());
        Assert.Equal(1, a.GetProperty("new").GetInt32());
        Assert.Equal(2, a.GetProperty("open").GetInt32()); // open and in progress
        Assert.Equal(2, a.GetProperty("closed").GetInt32());
        Assert.Equal(26.0, a.GetProperty("avgResolutionHours").GetDouble()); // (4 h + 48 h) / 2
        Assert.Equal(50.0, a.GetProperty("slaComplianceRate").GetDouble()); // one of the two closed was on time
        Assert.Equal(2, a.GetProperty("previousTotal").GetInt32());
        Assert.Equal(62.5, a.GetProperty("sharePercent").GetDouble()); // 5 of 8

        var b = Of(items, s.B.Name);
        Assert.Equal(2, b.GetProperty("total").GetInt32());
        Assert.Equal(4, b.GetProperty("previousTotal").GetInt32());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("slaComplianceRate").ValueKind); // nothing closed: no data, not 100 %
        Assert.Equal(0.0, b.GetProperty("avgResolutionHours").GetDouble());

        var c = Of(items, s.C.Name);
        Assert.Equal(3.0, c.GetProperty("avgResolutionHours").GetDouble());
        Assert.Equal(JsonValueKind.Null, c.GetProperty("slaComplianceRate").ValueKind); // closed, but with no due date to measure against
        Assert.Equal(12.5, c.GetProperty("sharePercent").GetDouble());
    }

    [SqlFact]
    public async Task The_top_is_never_more_than_ten_and_the_companies_are_ranked_by_tickets_whatever_limit_is_asked()
    {
        const int year = 2040;
        var companies = Enumerable.Range(1, 12).Select(i => new Company { Name = Mark + $"Firma {i:00} {year}" }).ToList();
        await _world.Db(async db => { db.Companies.AddRange(companies); return await db.SaveChangesAsync(); });
        var tickets = new List<Ticket>();
        for (var i = 0; i < 12; i++)
            for (var n = 0; n < 12 - i; n++) tickets.Add(Made(companies[i], _world.CliX, At(year, 3, 5), TicketStatus.New, _world.BranchA)); // 12, 11, ... 1
        await _world.Db(async db => { db.Tickets.AddRange(tickets); return await db.SaveChangesAsync(); });

        var top = await GetAsync(_world.Admin, $"{Base}/top?from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z&limit=1000");

        Assert.Equal(10, top.GetProperty("items").GetArrayLength());
        Assert.Equal(78, top.GetProperty("totalTickets").GetInt32());
        Assert.Equal(75, top.GetProperty("topTickets").GetInt32()); // 12 + 11 + ... + 3
        Assert.Equal(12, top.GetProperty("totalCompanies").GetInt32());
        Assert.Equal(companies[0].Name, top.GetProperty("items")[0].GetProperty("name").GetString());

        var one = await GetAsync(_world.Admin, $"{Base}/top?from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z&limit=1");
        Assert.Equal(1, one.GetProperty("items").GetArrayLength());
    }

    [SqlFact]
    public async Task Without_a_company_the_other_figures_are_for_the_ten_companies_of_the_top_and_nobody_else()
    {
        const int year = 2041;
        var companies = Enumerable.Range(1, 12).Select(i => new Company { Name = Mark + $"Firma {i:00} {year}" }).ToList();
        await _world.Db(async db => { db.Companies.AddRange(companies); return await db.SaveChangesAsync(); });
        var tickets = new List<Ticket>();
        for (var i = 0; i < 12; i++)
            for (var n = 0; n < 12 - i; n++) tickets.Add(Made(companies[i], _world.CliX, At(year, 3, 5), TicketStatus.New, _world.BranchA));
        await _world.Db(async db => { db.Tickets.AddRange(tickets); return await db.SaveChangesAsync(); });
        var period = $"from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z";

        var topics = await GetAsync(_world.Admin, $"{Base}/topics?{period}");
        var branches = await GetAsync(_world.Admin, $"{Base}/branches?{period}");
        var series = await GetAsync(_world.Admin, $"{Base}/timeseries?{period}");

        Assert.Equal(75, topics.EnumerateArray().Sum(i => i.GetProperty("count").GetInt32())); // not the 78 of all twelve
        Assert.Equal(75, branches.EnumerateArray().Sum(i => i.GetProperty("count").GetInt32()));
        Assert.Equal(75, series.EnumerateArray().Sum(p => p.GetProperty("opened").GetInt32()));
    }

    [SqlFact]
    public async Task The_list_of_all_companies_is_paged_searched_and_sorted_and_a_page_is_never_bigger_than_a_hundred()
    {
        var s = await SceneAsync(2032);
        var p = s.Period;

        var byName = await GetAsync(_world.Admin, $"{Base}/list?{p}&search={Mark.Trim()}%20&sort=name&descending=false&pageSize=2");
        var names = byName.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
        Assert.Equal(2, names.Count);
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
        Assert.Equal(3, byName.GetProperty("totalCount").GetInt32()); // the three of this test: the search narrowed the list to them

        var second = await GetAsync(_world.Admin, $"{Base}/list?{p}&search={Mark.Trim()}%20&sort=name&descending=false&pageSize=2&page=2");
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());

        var byTotal = await GetAsync(_world.Admin, $"{Base}/list?{p}&search={s.A.Name.Replace(" ", "%20")}");
        Assert.Equal(s.A.Name, byTotal.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(5, byTotal.GetProperty("items")[0].GetProperty("total").GetInt32());

        var growth = await GetAsync(_world.Admin, $"{Base}/list?{p}&search={Mark.Trim()}%20&sort=growth&descending=false");
        Assert.Equal(s.B.Name, growth.GetProperty("items")[0].GetProperty("name").GetString()); // B went from 4 to 2: the biggest fall comes first when it is ascending

        var huge = await GetAsync(_world.Admin, $"{Base}/list?{p}&pageSize=5000");
        Assert.Equal(100, huge.GetProperty("pageSize").GetInt32());
        var nonsense = await GetAsync(_world.Admin, $"{Base}/list?{p}&sort=drop%20table&page=-4");
        Assert.Equal(1, nonsense.GetProperty("page").GetInt32());
    }

    [SqlFact]
    public async Task A_company_with_nothing_in_the_period_is_on_the_list_with_zeros_not_missing()
    {
        var s = await SceneAsync(2033);

        var page = await GetAsync(_world.Admin, $"{Base}/list?from=2033-06-01T00:00:00Z&to=2033-06-30T23:59:59Z&search={s.C.Name.Replace(" ", "%20")}");

        var row = page.GetProperty("items")[0];
        Assert.Equal(s.C.Name, row.GetProperty("name").GetString());
        Assert.Equal(0, row.GetProperty("total").GetInt32());
        Assert.Equal(0.0, row.GetProperty("sharePercent").GetDouble());
    }

    // ============================ one company, or one person of it ============================

    [SqlFact]
    public async Task The_summary_of_a_company_and_of_one_of_its_people_are_counted_by_hand()
    {
        var s = await SceneAsync(2034);

        var company = await GetAsync(_world.EmpA1, $"{Base}/summary?{s.Period}&companyId={s.A.Id}");
        Assert.Equal(5, company.GetProperty("total").GetInt32());
        Assert.Equal(2, company.GetProperty("closed").GetInt32());
        Assert.Equal(2, company.GetProperty("previousTotal").GetInt32());

        var person = await GetAsync(_world.EmpA1, $"{Base}/summary?{s.Period}&companyId={s.A.Id}&clientId={_world.CliX.Id}");
        Assert.Equal(3, person.GetProperty("total").GetInt32()); // X made three of the five
        Assert.Equal(1, person.GetProperty("open").GetInt32());
        Assert.Equal(2, person.GetProperty("closed").GetInt32());
        Assert.Equal(1, person.GetProperty("previousTotal").GetInt32()); // one of A's two in February was X's
    }

    [SqlFact]
    public async Task The_people_of_a_company_are_ranked_by_the_tickets_they_made()
    {
        var s = await SceneAsync(2035);

        var people = await GetAsync(_world.Admin, $"{Base}/clients?{s.Period}&companyId={s.A.Id}");

        Assert.Equal(2, people.GetArrayLength());
        Assert.Equal(_world.CliX.Id, people[0].GetProperty("clientId").GetGuid());
        Assert.Equal(3, people[0].GetProperty("total").GetInt32());
        Assert.Equal(1, people[0].GetProperty("open").GetInt32());
        Assert.Equal(2, people[0].GetProperty("closed").GetInt32());
        Assert.Equal(2, people[1].GetProperty("total").GetInt32());
        Assert.Equal(1, people[1].GetProperty("open").GetInt32()); // the one in progress; the new one is neither open nor closed yet

        var limited = await GetAsync(_world.Admin, $"{Base}/clients?{s.Period}&companyId={s.A.Id}&limit=1");
        Assert.Equal(1, limited.GetArrayLength());
    }

    // ============================ topics, categories, branches: two lists, and the rows add up ============================

    [SqlFact]
    public async Task Topics_and_categories_are_two_lists_by_id_and_a_ticket_without_one_is_a_row_of_its_own_so_the_rows_add_up()
    {
        var s = await SceneAsync(2036);

        var topics = await GetAsync(_world.Admin, $"{Base}/topics?{s.Period}&companyId={s.A.Id}");
        var categories = await GetAsync(_world.Admin, $"{Base}/categories?{s.Period}&companyId={s.A.Id}");

        Assert.Equal(5, topics.EnumerateArray().Sum(i => i.GetProperty("count").GetInt32()));
        Assert.Equal(5, categories.EnumerateArray().Sum(i => i.GetProperty("count").GetInt32()));

        Assert.Equal(2, Of(topics, _t1.Name).GetProperty("count").GetInt32());
        Assert.Equal(1, Of(topics, _t2.Name).GetProperty("count").GetInt32());
        var noTopic = topics.EnumerateArray().Single(i => i.GetProperty("id").ValueKind == JsonValueKind.Null);
        Assert.Equal(2, noTopic.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, noTopic.GetProperty("name").ValueKind);
        Assert.Equal(new[] { _t1.Name, null, _t2.Name }, topics.EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToArray()); // most first; "none" last among equals

        Assert.Equal(2, Of(categories, _k1.Name).GetProperty("count").GetInt32());
        Assert.Equal(3, categories.EnumerateArray().Single(i => i.GetProperty("id").ValueKind == JsonValueKind.Null).GetProperty("count").GetInt32());
    }

    [SqlFact]
    public async Task A_topic_that_is_switched_off_keeps_its_name_and_the_tickets_it_had()
    {
        var s = await SceneAsync(2037);
        await _world.Db(db => db.HelpTopics.Where(h => h.Id == _t1.Id).ExecuteUpdateAsync(x => x.SetProperty(h => h.IsActive, false)));

        var topics = await GetAsync(_world.Admin, $"{Base}/topics?{s.Period}&companyId={s.A.Id}");

        var row = Of(topics, _t1.Name);
        Assert.Equal(2, row.GetProperty("count").GetInt32());
        Assert.False(row.GetProperty("isActive").GetBoolean());
    }

    [SqlFact]
    public async Task The_branches_are_counted_with_their_sub_branches_and_what_has_none()
    {
        var s = await SceneAsync(2038);

        var branches = await GetAsync(_world.Admin, $"{Base}/branches?{s.Period}&companyId={s.A.Id}");

        Assert.Equal(5, branches.EnumerateArray().Sum(b => b.GetProperty("count").GetInt32()));
        var a = branches.EnumerateArray().Single(b => b.GetProperty("departmentId").GetGuid() == _world.BranchA);
        Assert.Equal(3, a.GetProperty("count").GetInt32());
        Assert.Equal(2, a.GetProperty("noSubBranchCount").GetInt32());
        Assert.Equal(1, a.GetProperty("subBranches")[0].GetProperty("count").GetInt32());
        Assert.Equal(_sub.Name, a.GetProperty("subBranches")[0].GetProperty("name").GetString());
        Assert.Equal(2, branches.EnumerateArray().Single(b => b.GetProperty("departmentId").GetGuid() == _world.BranchB).GetProperty("count").GetInt32());
    }

    // ============================ who solves it: tickets closed in the period ============================

    [SqlFact]
    public async Task Who_closes_is_counted_among_the_tickets_closed_in_the_period_whenever_they_were_made_and_by_the_branch_of_the_ticket()
    {
        var s = await SceneAsync(2039);

        var closed = await GetAsync(_world.Admin, $"{Base}/closed-by?{s.Period}&companyId={s.A.Id}");

        Assert.Equal(3, closed.GetProperty("totalClosed").GetInt32()); // two of March and the one of February that was closed in March
        var people = closed.GetProperty("people");
        Assert.Equal(_world.EmpA1.Id, people[0].GetProperty("userId").GetGuid());
        Assert.Equal(2, people[0].GetProperty("closedCount").GetInt32());
        Assert.Equal(2, people[0].GetProperty("branches").GetArrayLength()); // one of the branch A, one of the branch B
        Assert.Equal(_world.EmpB1.Id, people[1].GetProperty("userId").GetGuid());
        Assert.Equal(1, people[1].GetProperty("closedCount").GetInt32());

        var branches = closed.GetProperty("branches");
        var a = branches.EnumerateArray().Single(b => b.GetProperty("departmentId").GetGuid() == _world.BranchA);
        Assert.Equal(2, a.GetProperty("count").GetInt32());
        Assert.Equal(1, a.GetProperty("subBranches")[0].GetProperty("count").GetInt32());
        Assert.Equal(1, branches.EnumerateArray().Single(b => b.GetProperty("departmentId").GetGuid() == _world.BranchB).GetProperty("count").GetInt32());
    }

    [SqlFact]
    public async Task The_time_series_counts_what_was_opened_on_a_day_and_what_was_closed_on_it()
    {
        var s = await SceneAsync(2042);

        var series = await GetAsync(_world.Admin, $"{Base}/timeseries?{s.Period}&companyId={s.A.Id}");

        // Days are the calendar days of the business: when the clocks go forward in March the end of the period falls into the first day of April.
        Assert.InRange(series.GetArrayLength(), 31, 32);
        Assert.Equal(5, series.EnumerateArray().Sum(p => p.GetProperty("opened").GetInt32()));
        Assert.Equal(3, series.EnumerateArray().Sum(p => p.GetProperty("closed").GetInt32()));
        var third = series.EnumerateArray().Single(p => p.GetProperty("date").GetString() == "2042-03-03");
        Assert.Equal(2, third.GetProperty("opened").GetInt32());
        Assert.Equal(1, third.GetProperty("closed").GetInt32());
    }

    // ============================ who may see what ============================

    [SqlFact]
    public async Task Staff_of_every_branch_get_the_same_figures_for_all_branches_while_the_old_report_and_the_list_of_tickets_stay_limited_to_their_own()
    {
        var s = await SceneAsync(2043);
        var range = $"from={s.Year}-03-01T00:00:00Z&to={s.Year}-03-31T23:59:59Z";

        // the new figures: both employees, and the administrator, see all five tickets of A, those of the other branch included
        foreach (var who in new[] { _world.EmpA1, _world.EmpB1, _world.Admin })
            Assert.Equal(5, (await GetAsync(who, $"{Base}/summary?{range}&companyId={s.A.Id}")).GetProperty("total").GetInt32());

        // the old report for the same company: an employee of branch A sees only branch A (a1, a2 closed, a5 in progress); the administrator sees all
        var old = await GetAsync(_world.EmpA1, $"/api/reports/summary?{range}&companyId={s.A.Id}");
        Assert.Equal(0, old.GetProperty("totalNew").GetInt32()); // the new one is in branch B
        Assert.Equal(1, old.GetProperty("totalOpen").GetInt32());
        Assert.Equal(2, old.GetProperty("totalClosed").GetInt32());
        var all = await GetAsync(_world.Admin, $"/api/reports/summary?{range}&companyId={s.A.Id}");
        Assert.Equal(1, all.GetProperty("totalNew").GetInt32());

        // the list of tickets: three of the five (those of branch A), never the two of branch B
        var list = await GetAsync(_world.EmpA1, $"/api/tickets?companyId={s.A.Id}&createdFrom={s.Year}-03-01T00:00:00Z&createdTo={s.Year}-03-31T23:59:59Z&pageSize=50");
        Assert.Equal(3, list.GetProperty("items").GetArrayLength());
        var branchB = await _world.Db(db => db.Tickets.Where(t => t.CompanyId == s.A.Id && t.DepartmentId == _world.BranchB).Select(t => t.TicketNumber).ToListAsync());
        var listed = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("ticketNumber").GetString()).ToHashSet();
        Assert.DoesNotContain(branchB, n => listed.Contains(n));
    }

    [SqlFact]
    public async Task A_client_is_refused_every_one_of_them_and_a_person_who_is_not_signed_in_too()
    {
        var s = await SceneAsync(2044);
        var routes = new[] { "top", "list", "summary", "clients", "topics", "categories", "branches", "closed-by", "timeseries", "export" };

        foreach (var route in routes)
        {
            var path = $"{Base}/{route}?{s.Period}&companyId={s.A.Id}";
            Assert.Equal(403, (await _world.Send(_world.CliX, HttpMethod.Get, path)).Code);
            Assert.Equal(401, (await _world.Send(null, HttpMethod.Get, path)).Code);
        }
    }

    // ============================ what is asked has to make sense ============================

    [SqlFact]
    public async Task A_person_is_never_asked_for_without_a_company_and_a_company_has_to_exist()
    {
        var s = await SceneAsync(2045);

        var noCompany = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/summary?{s.Period}&clientId={_world.CliX.Id}");
        var noCompanyAtAll = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/summary?{s.Period}");
        var onlyClient = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/topics?{s.Period}&clientId={_world.CliX.Id}");
        var unknown = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/topics?{s.Period}&companyId={Guid.NewGuid()}");
        var reversed = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/top?from={s.Year}-04-01T00:00:00Z&to={s.Year}-03-01T00:00:00Z");

        Assert.Equal(400, noCompany.Code);
        Assert.Equal(400, noCompanyAtAll.Code);
        Assert.Equal(400, onlyClient.Code);
        Assert.Equal(404, unknown.Code);
        Assert.Equal(400, reversed.Code);
        Assert.Contains("Choose a company first", noCompany.Text);
    }

    [SqlFact]
    public async Task A_period_with_nothing_in_it_gives_empty_lists_and_zeros_not_an_error_or_a_division_by_zero()
    {
        var s = await SceneAsync(2046);
        const string empty = "from=2099-01-01T00:00:00Z&to=2099-01-31T23:59:59Z";

        var top = await GetAsync(_world.Admin, $"{Base}/top?{empty}");
        var summary = await GetAsync(_world.Admin, $"{Base}/summary?{empty}&companyId={s.A.Id}");

        Assert.Equal(0, top.GetProperty("items").GetArrayLength());
        Assert.Equal(0, top.GetProperty("totalTickets").GetInt32());
        Assert.Equal(0, summary.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("slaComplianceRate").ValueKind);
        Assert.Equal(0, (await GetAsync(_world.Admin, $"{Base}/topics?{empty}&companyId={s.A.Id}")).GetArrayLength());
        Assert.Equal(0, (await GetAsync(_world.Admin, $"{Base}/clients?{empty}&companyId={s.A.Id}")).GetArrayLength());
        Assert.Equal(0, (await GetAsync(_world.Admin, $"{Base}/closed-by?{empty}&companyId={s.A.Id}")).GetProperty("totalClosed").GetInt32());
    }

    // ============================ the workbook ============================

    [SqlFact]
    public async Task The_workbook_has_a_sheet_for_each_list_and_the_company_row_says_what_the_screen_says()
    {
        var s = await SceneAsync(2048);

        var file = await _world.GetBytes(_world.Admin, $"{Base}/export?{s.Period}");

        Assert.Equal(200, file.Code);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        using var book = new XSSFWorkbook(new MemoryStream(file.Bytes));
        var names = Enumerable.Range(0, book.NumberOfSheets).Select(book.GetSheetName).ToList();
        Assert.Contains("About", names);
        Assert.Contains("Companies", names);
        Assert.Contains("Topics", names);
        Assert.Contains("Closed by", names);

        var sheet = book.GetSheet("Companies");
        var rows = Enumerable.Range(1, sheet.LastRowNum).Select(sheet.GetRow).ToList();
        var alfa = rows.Single(r => r.GetCell(0).StringCellValue == s.A.Name);
        Assert.Equal(5, alfa.GetCell(1).NumericCellValue);
        Assert.Equal(26.0, alfa.GetCell(5).NumericCellValue);

        var oneCompany = await _world.GetBytes(_world.Admin, $"{Base}/export?{s.Period}&companyId={s.A.Id}");
        using var companyBook = new XSSFWorkbook(new MemoryStream(oneCompany.Bytes));
        Assert.NotNull(companyBook.GetSheet("Clients"));
    }
}
