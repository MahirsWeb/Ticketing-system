using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The extra statistics of the companies: open tickets by age, time written down, replies and pick-up, the rhythm of the week, repeated reports, companies gone quiet.
/// Each test makes its companies and its tickets in a year of its own, so that the figures can be counted by hand.
/// </summary>
[Collection(ApiCollection.Name)]
public class CompanyReportInsightsTests : IAsyncLifetime
{
    private const string Mark = "RI ";
    private const string Base = "/api/reports/companies";

    private readonly ApiWorld _world;
    private HelpTopic _t1 = null!, _t2 = null!;

    public CompanyReportInsightsTests(ApiWorld world) => _world = world;

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        _t1 = new HelpTopic { Name = Mark + "mreža" };
        _t2 = new HelpTopic { Name = Mark + "štampač" };
        await _world.Db(async db => { db.AddRange(_t1, _t2); return await db.SaveChangesAsync(); });
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => t.TicketNumber.StartsWith("RI-")).ExecuteDeleteAsync(); // the messages and the work sessions go with their tickets
            await db.Companies.Where(c => c.Name.StartsWith(Mark)).ExecuteDeleteAsync();
            return await db.HelpTopics.Where(h => h.Name.StartsWith(Mark)).ExecuteDeleteAsync();
        });
    }

    private static DateTime At(int year, int month, int day, int hour = 9, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static string Period(int year) => $"from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z";

    private async Task<Company> CompanyAsync(string name)
    {
        var company = new Company { Name = Mark + name };
        await _world.Db(async db => { db.Companies.Add(company); return await db.SaveChangesAsync(); });
        return company;
    }

    private Ticket Made(Company company, Actor client, DateTime created, TicketStatus status = TicketStatus.Open, HelpTopic? topic = null, double? openedAfterHours = null) =>
        new()
        {
            TicketNumber = "RI-" + Guid.NewGuid().ToString("N")[..10],
            Title = "statistika",
            Description = "<p>x</p>",
            ClientId = client.Id,
            CompanyId = company.Id,
            Status = status,
            DepartmentId = _world.BranchA,
            HelpTopicId = topic?.Id,
            CreatedAt = created,
            OpenedAtUtc = openedAfterHours.HasValue ? created.AddHours(openedAfterHours.Value) : null,
            ClosedAtUtc = status == TicketStatus.Closed ? created.AddHours(2) : null,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };

    private async Task SaveAsync(params object[] entities) => await _world.Db(async db => { db.AddRange(entities); return await db.SaveChangesAsync(); });

    private async Task<JsonElement> GetAsync(Actor? who, string path, int expected = 200)
    {
        var response = await _world.Send(who, HttpMethod.Get, path);
        Assert.True(response.Code == expected, $"{path} -> {response.Code}: {response.Text}");
        return string.IsNullOrEmpty(response.Text) ? default : JsonDocument.Parse(response.Text).RootElement.Clone();
    }

    // ============================ open now, by age ============================

    [SqlFact]
    public async Task Open_tickets_are_counted_by_how_long_ago_they_were_created_and_a_closed_one_is_not_counted()
    {
        var a = await CompanyAsync("Otvoreni 2050");
        var now = DateTime.UtcNow;
        await SaveAsync(
            Made(a, _world.CliX, now.AddHours(-20), TicketStatus.New),          // up to 2 days
            Made(a, _world.CliX, now.AddDays(-5), TicketStatus.Open),           // up to 7
            Made(a, _world.CliX, now.AddDays(-6.5), TicketStatus.InProgress),   // up to 7
            Made(a, _world.CliX, now.AddDays(-20), TicketStatus.Resolved),      // up to 30 (resolved is not closed yet)
            Made(a, _world.CliX, now.AddDays(-60), TicketStatus.Open),          // older: the oldest
            Made(a, _world.CliX, now.AddDays(-90), TicketStatus.Closed));       // closed: not open any more, however old

        var age = await GetAsync(_world.Admin, $"{Base}/open-age?companyId={a.Id}");

        Assert.Equal(5, age.GetProperty("total").GetInt32());
        Assert.Equal(1, age.GetProperty("upTo2Days").GetInt32());
        Assert.Equal(2, age.GetProperty("upTo7Days").GetInt32());
        Assert.Equal(1, age.GetProperty("upTo30Days").GetInt32());
        Assert.Equal(1, age.GetProperty("older").GetInt32());
        Assert.Equal(60, age.GetProperty("oldestDays").GetInt32());
        Assert.Equal(a.Name, age.GetProperty("oldestCompany").GetString());
        Assert.DoesNotContain("ticketNumber", age.GetRawText(), StringComparison.OrdinalIgnoreCase); // an age and a company, never a ticket
    }

    [SqlFact]
    public async Task Open_age_of_a_company_with_nothing_open_is_zero_with_no_oldest()
    {
        var a = await CompanyAsync("Zatvoreni 2051");
        await SaveAsync(Made(a, _world.CliX, DateTime.UtcNow.AddDays(-3), TicketStatus.Closed));

        var age = await GetAsync(_world.Admin, $"{Base}/open-age?companyId={a.Id}");

        Assert.Equal(0, age.GetProperty("total").GetInt32());
        Assert.Equal(0, age.GetProperty("oldestDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, age.GetProperty("oldestCompany").ValueKind);
    }

    [SqlFact]
    public async Task Open_age_of_one_person_counts_only_their_tickets()
    {
        var a = await CompanyAsync("Osoba 2052");
        var now = DateTime.UtcNow;
        await SaveAsync(Made(a, _world.CliX, now.AddDays(-1)), Made(a, _world.CliX, now.AddDays(-10)), Made(a, _world.CliY, now.AddDays(-1)));

        var x = await GetAsync(_world.Admin, $"{Base}/open-age?companyId={a.Id}&clientId={_world.CliX.Id}");
        var all = await GetAsync(_world.Admin, $"{Base}/open-age?companyId={a.Id}");

        Assert.Equal(2, x.GetProperty("total").GetInt32());
        Assert.Equal(3, all.GetProperty("total").GetInt32());
    }

    // ============================ time written down ============================

    [SqlFact]
    public async Task Time_spent_adds_up_the_finished_work_sessions_that_started_in_the_period_and_counts_a_running_one_apart()
    {
        const int year = 2032;
        var a = await CompanyAsync("Vrijeme 2032");
        var t1 = Made(a, _world.CliX, At(year, 3, 3));
        var t2 = Made(a, _world.CliX, At(year, 3, 4));
        var other = Made(await CompanyAsync("Drugo vrijeme 2032"), _world.CliX, At(year, 3, 4));
        await SaveAsync(t1, t2, other);
        var who = _world.EmpA1.Id;
        await SaveAsync(
            new TicketWorkSession { TicketId = t1.Id, UserId = who, StartedAtUtc = At(year, 3, 5, 9), EndedAtUtc = At(year, 3, 5, 11) },          // 2 h
            new TicketWorkSession { TicketId = t1.Id, UserId = who, StartedAtUtc = At(year, 3, 5, 13), EndedAtUtc = At(year, 3, 5, 14, 30) },     // 1.5 h
            new TicketWorkSession { TicketId = t2.Id, UserId = who, StartedAtUtc = At(year, 3, 6, 10), EndedAtUtc = At(year, 3, 6, 10, 30) },     // 0.5 h
            new TicketWorkSession { TicketId = t2.Id, UserId = who, StartedAtUtc = At(year, 3, 7, 10), EndedAtUtc = null },                        // running
            new TicketWorkSession { TicketId = t1.Id, UserId = who, StartedAtUtc = At(year, 2, 20, 9), EndedAtUtc = At(year, 2, 20, 17) },        // before the period
            new TicketWorkSession { TicketId = other.Id, UserId = who, StartedAtUtc = At(year, 3, 5, 9), EndedAtUtc = At(year, 3, 5, 19) });      // another company

        var time = await GetAsync(_world.Admin, $"{Base}/time-spent?{Period(year)}&companyId={a.Id}");

        Assert.Equal(4.0, time.GetProperty("totalHours").GetDouble());
        Assert.Equal(3, time.GetProperty("sessions").GetInt32());
        Assert.Equal(2, time.GetProperty("tickets").GetInt32());
        Assert.Equal(2.0, time.GetProperty("avgHoursPerTicket").GetDouble());
        Assert.Equal(1, time.GetProperty("runningSessions").GetInt32());
    }

    [SqlFact]
    public async Task Time_spent_is_zero_when_nothing_was_written_down()
    {
        var a = await CompanyAsync("Bez vremena 2033");

        var time = await GetAsync(_world.Admin, $"{Base}/time-spent?{Period(2033)}&companyId={a.Id}");

        Assert.Equal(0.0, time.GetProperty("totalHours").GetDouble());
        Assert.Equal(0, time.GetProperty("sessions").GetInt32());
        Assert.Equal(0.0, time.GetProperty("avgHoursPerTicket").GetDouble());
    }

    // ============================ replies and pick-up ============================

    [SqlFact]
    public async Task Conversation_counts_only_replies_and_averages_the_wait_of_the_tickets_that_were_picked_up()
    {
        const int year = 2034;
        var a = await CompanyAsync("Razgovor 2034");
        var t1 = Made(a, _world.CliX, At(year, 3, 3), TicketStatus.Open, null, 1);
        var t2 = Made(a, _world.CliX, At(year, 3, 4), TicketStatus.Open, null, 3);
        var t3 = Made(a, _world.CliX, At(year, 3, 5), TicketStatus.New); // nobody picked it up yet
        await SaveAsync(t1, t2, t3);
        var author = _world.EmpA1.Id;
        TicketMessage M(Ticket t, MessageType type) => new() { TicketId = t.Id, AuthorId = author, Type = type, BodyHtml = "<p>x</p>", CreatedAt = At(year, 3, 6) };
        await SaveAsync(M(t1, MessageType.Response), M(t1, MessageType.Response), M(t1, MessageType.InternalNote), M(t1, MessageType.SystemEvent), M(t2, MessageType.Response));

        var talk = await GetAsync(_world.Admin, $"{Base}/conversation?{Period(year)}&companyId={a.Id}");

        Assert.Equal(3, talk.GetProperty("tickets").GetInt32());
        Assert.Equal(1.0, talk.GetProperty("avgMessagesPerTicket").GetDouble()); // 3 replies, 3 tickets: notes and events do not count
        Assert.Equal(2, talk.GetProperty("pickedUp").GetInt32());
        Assert.Equal(2.0, talk.GetProperty("avgHoursToPickup").GetDouble()); // (1 h + 3 h) / 2
        Assert.Equal(1, talk.GetProperty("notPickedUp").GetInt32());
    }

    // ============================ the rhythm of the week ============================

    [SqlFact]
    public async Task Rhythm_counts_by_weekday_and_hour_in_the_business_time_zone_not_in_utc()
    {
        const int year = 2036; // 3 March is a Monday, 9 March a Sunday; the clocks change at the end of the month, so it is UTC+1 all through these days
        var a = await CompanyAsync("Ritam 2036");
        await SaveAsync(
            Made(a, _world.CliX, At(year, 3, 3, 8, 30)),    // Monday 09:30
            Made(a, _world.CliX, At(year, 3, 3, 10, 10)),   // Monday 11:10
            Made(a, _world.CliX, At(year, 3, 9, 23, 30)),   // Sunday 23:30 in UTC, but Monday 00:30 in Sarajevo
            Made(a, _world.CliX, At(year, 3, 9, 12, 0)));   // Sunday 13:00

        var rhythm = await GetAsync(_world.Admin, $"{Base}/rhythm?{Period(year)}&companyId={a.Id}");

        Assert.Equal(4, rhythm.GetProperty("total").GetInt32());
        Assert.Equal(new[] { 3, 0, 0, 0, 0, 0, 1 }, rhythm.GetProperty("byWeekday").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        var hours = rhythm.GetProperty("byHour").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(24, hours.Length);
        Assert.Equal(new[] { 0, 9, 11, 13 }, hours.Select((n, h) => (n, h)).Where(x => x.n == 1).Select(x => x.h).ToArray());
        Assert.False(string.IsNullOrEmpty(rhythm.GetProperty("timeZone").GetString()));
    }

    // ============================ the same person, the same topic ============================

    [SqlFact]
    public async Task Repeated_reports_are_the_same_person_with_the_same_topic_more_than_once()
    {
        const int year = 2035;
        var a = await CompanyAsync("Ponovljeno 2035");
        var tickets = new List<Ticket>();
        for (var i = 0; i < 3; i++) tickets.Add(Made(a, _world.CliX, At(year, 3, 3 + i), TicketStatus.Open, _t1)); // X, mreža: three times
        for (var i = 0; i < 2; i++) tickets.Add(Made(a, _world.CliX, At(year, 3, 10 + i), TicketStatus.Open, _t2)); // X, štampač: twice
        tickets.Add(Made(a, _world.CliY, At(year, 3, 12), TicketStatus.Open, _t1));                                  // Y, mreža: once, not a repeat
        tickets.Add(Made(a, _world.CliY, At(year, 3, 13), TicketStatus.Open, _t2));
        tickets.Add(Made(a, _world.CliY, At(year, 3, 14), TicketStatus.Open, _t2));                                  // Y, štampač: twice
        tickets.Add(Made(a, _world.CliY, At(year, 3, 15), TicketStatus.Open));                                       // no topic: never a repeat
        tickets.Add(Made(a, _world.CliY, At(year, 3, 16), TicketStatus.Open));
        await SaveAsync(tickets.Cast<object>().ToArray());

        var repeated = await GetAsync(_world.Admin, $"{Base}/repeated-topics?{Period(year)}&companyId={a.Id}");

        Assert.Equal(3, repeated.GetProperty("groups").GetInt32());
        Assert.Equal(7, repeated.GetProperty("tickets").GetInt32()); // 3 + 2 + 2
        var items = repeated.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(3, items[0].GetProperty("count").GetInt32());
        Assert.Equal(_t1.Name, items[0].GetProperty("topicName").GetString());
        Assert.Equal(_world.CliX.Id, items[0].GetProperty("clientId").GetGuid());
        Assert.All(items.Skip(1), i => Assert.Equal(2, i.GetProperty("count").GetInt32()));

        var onlyY = await GetAsync(_world.Admin, $"{Base}/repeated-topics?{Period(year)}&companyId={a.Id}&clientId={_world.CliY.Id}");
        Assert.Equal(1, onlyY.GetProperty("groups").GetInt32());
        var one = await GetAsync(_world.Admin, $"{Base}/repeated-topics?{Period(year)}&companyId={a.Id}&limit=1");
        Assert.Single(one.GetProperty("items").EnumerateArray());
        Assert.Equal(3, one.GetProperty("groups").GetInt32()); // the total is of all of them, the list is cut
    }

    // ============================ companies gone quiet ============================

    [SqlFact]
    public async Task Quiet_companies_had_tickets_in_the_period_before_and_have_none_now_most_before_first()
    {
        const int year = 2037;
        var a = await CompanyAsync("Tiha A 2037");
        var b = await CompanyAsync("Tiha B 2037");
        var c = await CompanyAsync("Nova C 2037");
        var d = await CompanyAsync("Stalna D 2037");
        await SaveAsync(
            Made(a, _world.CliX, At(year, 2, 10)), Made(a, _world.CliX, At(year, 2, 11)),                                                                       // before: 2, now: none
            Made(b, _world.CliX, At(year, 2, 5)), Made(b, _world.CliX, At(year, 2, 6)), Made(b, _world.CliX, At(year, 2, 7)), Made(b, _world.CliX, At(year, 2, 8)), // before: 4, now: none
            Made(c, _world.CliX, At(year, 3, 10)),                                                                                                              // now only
            Made(d, _world.CliX, At(year, 2, 10)), Made(d, _world.CliX, At(year, 3, 10)));                                                                      // both

        var quiet = await GetAsync(_world.Admin, $"{Base}/quiet?{Period(year)}");

        Assert.Equal(2, quiet.GetProperty("total").GetInt32());
        var items = quiet.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { b.Name, a.Name }, items.Select(i => i.GetProperty("name").GetString()).ToArray());
        Assert.Equal(4, items[0].GetProperty("previousTotal").GetInt32());
        Assert.Equal(DateTime.Parse("2037-02-08T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal), items[0].GetProperty("lastTicketAtUtc").GetDateTime().ToUniversalTime());
    }

    // ============================ who may ask, and what is refused ============================

    [SqlFact]
    public async Task Staff_of_any_branch_get_the_same_figures_and_a_client_gets_nothing()
    {
        var a = await CompanyAsync("Pristup 2053");
        await SaveAsync(Made(a, _world.CliX, DateTime.UtcNow.AddDays(-4)));
        string[] routes = { "open-age", "time-spent", "conversation", "rhythm", "repeated-topics" };

        foreach (var route in routes)
        {
            var admin = await GetAsync(_world.Admin, $"{Base}/{route}?companyId={a.Id}");
            var employee = await GetAsync(_world.EmpB1, $"{Base}/{route}?companyId={a.Id}"); // a branch that has none of these tickets
            Assert.Equal(admin.GetRawText(), employee.GetRawText());
            await GetAsync(_world.CliX, $"{Base}/{route}?companyId={a.Id}", 403);
            await GetAsync(null, $"{Base}/{route}?companyId={a.Id}", 401);
        }
        await GetAsync(_world.CliX, $"{Base}/quiet", 403);
    }

    [SqlFact]
    public async Task A_person_without_a_company_and_a_company_that_does_not_exist_are_refused()
    {
        foreach (var route in new[] { "open-age", "time-spent", "conversation", "rhythm", "repeated-topics" })
        {
            await GetAsync(_world.Admin, $"{Base}/{route}?clientId={_world.CliX.Id}", 400);
            await GetAsync(_world.Admin, $"{Base}/{route}?companyId={Guid.NewGuid()}", 404);
        }
    }

    [SqlFact]
    public async Task A_period_that_runs_backwards_is_refused_by_the_new_routes_too()
    {
        foreach (var route in new[] { "open-age", "time-spent", "conversation", "rhythm", "repeated-topics", "quiet" })
            await GetAsync(_world.Admin, $"{Base}/{route}?from=2030-03-31T00:00:00Z&to=2030-03-01T00:00:00Z", 400);
    }
}
