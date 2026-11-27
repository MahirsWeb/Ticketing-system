using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The card of a company (first and last ticket, tickets by year, registered and active clients, hours) and the load of the branches that work for it (with
/// their sub-branches). Same rule as the other company statistics: all branches together, for Admin and Employee alike.
/// </summary>
[Collection(ApiCollection.Name)]
public class CompanyCardTests : IAsyncLifetime
{
    private const string Mark = "CC ";
    private const string Base = "/api/reports/companies";

    private readonly ApiWorld _world;
    private readonly List<Guid> _subBranches = new();

    public CompanyCardTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => t.TicketNumber.StartsWith("CC-")).ExecuteDeleteAsync(); // the sessions go with their tickets
            await db.Companies.Where(c => c.Name.StartsWith(Mark)).ExecuteDeleteAsync();
            return await db.SubBranches.Where(s => _subBranches.Contains(s.Id)).ExecuteDeleteAsync();
        });
    }

    private static DateTime At(int y, int m, int d, int h = 9, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private async Task<Company> CompanyAsync(string name)
    {
        var company = new Company { Name = Mark + name };
        await _world.Db(async db => { db.Companies.Add(company); return await db.SaveChangesAsync(); });
        return company;
    }

    private Ticket Made(Company company, Actor client, DateTime created, TicketStatus status = TicketStatus.Open, Guid? department = null, Guid? sub = null, DateTime? closed = null) =>
        new()
        {
            TicketNumber = "CC-" + Guid.NewGuid().ToString("N")[..10],
            Title = "firma",
            Description = "<p>x</p>",
            ClientId = client.Id,
            CompanyId = company.Id,
            Status = status,
            DepartmentId = department,
            SubBranchId = sub,
            CreatedAt = created,
            OpenedAtUtc = created,
            ClosedAtUtc = status == TicketStatus.Closed ? closed ?? created.AddHours(2) : null,
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

    private static TicketWorkSession Session(Ticket t, Actor who, DateTime start, double minutes) =>
        new() { TicketId = t.Id, UserId = who.Id, StartedAtUtc = start, EndedAtUtc = start.AddMinutes(minutes) };

    /// <summary>The two client accounts belong to the company for the test and go back to the company they had.</summary>
    private async Task WithClientsOfAsync(Company company, Func<Task> test, bool secondActive = true)
    {
        await _world.Db(db => db.Users.Where(u => u.Id == _world.CliX.Id || u.Id == _world.CliY.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CompanyId, company.Id)));
        if (!secondActive) await _world.Db(db => db.Users.Where(u => u.Id == _world.CliY.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)));
        try { await test(); }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliX.Id || u.Id == _world.CliY.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CompanyId, _world.CompanyId).SetProperty(x => x.IsActive, true)));
        }
    }

    // ============================ the card ============================

    [SqlFact]
    public async Task The_card_has_the_first_and_the_last_ticket_the_total_and_the_tickets_by_local_year()
    {
        var company = await CompanyAsync("Godine");
        await SaveAsync(
            Made(company, _world.CliY, At(2031, 12, 31, 23, 30)), // 00:30 on 1 January 2032 in Sarajevo: the year 2032
            Made(company, _world.CliX, At(2032, 5, 10)), Made(company, _world.CliX, At(2032, 6, 10)), Made(company, _world.CliX, At(2033, 2, 1)));

        var card = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}");

        Assert.Equal(At(2031, 12, 31, 23, 30), card.GetProperty("firstTicketAtUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(At(2033, 2, 1), card.GetProperty("lastTicketAtUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(4, card.GetProperty("totalTickets").GetInt32());
        var years = card.GetProperty("byYear").EnumerateArray().Select(y => (y.GetProperty("year").GetInt32(), y.GetProperty("count").GetInt32())).ToList();
        Assert.Equal(new[] { (2032, 3), (2033, 1) }, years);
        Assert.Equal(4, years.Sum(y => y.Item2)); // the years add up to the total
    }

    [SqlFact]
    public async Task A_company_with_no_tickets_has_an_empty_card_and_not_an_error()
    {
        var company = await CompanyAsync("Prazna");

        var card = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}");

        Assert.Equal(JsonValueKind.Null, card.GetProperty("firstTicketAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("lastTicketAtUtc").ValueKind);
        Assert.Equal(0, card.GetProperty("totalTickets").GetInt32());
        Assert.Equal(0, card.GetProperty("byYear").GetArrayLength());
        Assert.Equal(0.0, card.GetProperty("totalHours").GetDouble());
    }

    [SqlFact]
    public async Task Clients_registered_are_the_active_accounts_of_the_company_and_active_are_those_that_wrote_in_the_period_and_ever()
    {
        var company = await CompanyAsync("Klijenti");
        await SaveAsync(Made(company, _world.CliY, At(2031, 6, 1)), Made(company, _world.CliX, At(2032, 5, 10)), Made(company, _world.CliX, At(2033, 2, 1)));
        var period = "from=2033-01-01T00:00:00Z&to=2033-03-31T23:59:59Z";

        await WithClientsOfAsync(company, async () =>
        {
            var card = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}&{period}");

            Assert.Equal(2, card.GetProperty("registeredClients").GetInt32());
            Assert.Equal(1, card.GetProperty("activeClientsInPeriod").GetInt32()); // only X wrote in the first months of 2033
            Assert.Equal(2, card.GetProperty("activeClientsEver").GetInt32());
        });

        await WithClientsOfAsync(company, async () =>
        {
            var card = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}&{period}");

            Assert.Equal(1, card.GetProperty("registeredClients").GetInt32()); // a person whose account was switched off is not registered any more
            Assert.Equal(1, card.GetProperty("activeClientsEver").GetInt32()); // and is not counted among the active either
        }, secondActive: false);
    }

    [SqlFact]
    public async Task The_hours_are_all_the_work_ever_recorded_on_the_tickets_of_the_company_and_a_running_session_counts_up_to_now()
    {
        var company = await CompanyAsync("Sati");
        var a = Made(company, _world.CliX, At(2032, 5, 10));
        var b = Made(company, _world.CliX, At(2033, 5, 10));
        var other = Made(await CompanyAsync("Tudja"), _world.CliX, At(2033, 5, 10));
        await SaveAsync(a, b, other);
        await SaveAsync(Session(a, _world.EmpA1, At(2032, 5, 11), 60), Session(b, _world.EmpB1, At(2033, 5, 11), 30), Session(other, _world.EmpA1, At(2033, 5, 11), 600));

        var finished = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}");
        await SaveAsync(new TicketWorkSession { TicketId = b.Id, UserId = _world.EmpA1.Id, StartedAtUtc = DateTime.UtcNow.AddHours(-2), EndedAtUtc = null });
        var running = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}");

        Assert.Equal(1.5, finished.GetProperty("totalHours").GetDouble()); // 60 + 30 minutes; the other company's ten hours are not these
        Assert.InRange(running.GetProperty("totalHours").GetDouble(), 3.4, 3.6); // and two hours more, up to now
    }

    // ============================ the branches ============================

    private async Task<(Company Company, SubBranch Sub)> BranchSceneAsync(int year)
    {
        var company = await CompanyAsync("Poslovnice " + year);
        var sub = new SubBranch { DepartmentId = _world.BranchA, Name = Mark + "podgrana " + year };
        await SaveAsync(sub);
        _subBranches.Add(sub.Id);
        var t = At(year, 3, 10);
        var tickets = new List<Ticket>
        {
            Made(company, _world.CliX, t, TicketStatus.Closed, _world.BranchA, sub.Id),
            Made(company, _world.CliX, t, TicketStatus.Open, _world.BranchA, sub.Id),
            Made(company, _world.CliX, t, TicketStatus.Open, _world.BranchA), // in A, in none of its sub-branches
            Made(company, _world.CliX, t, TicketStatus.Closed, _world.BranchB),
            Made(company, _world.CliX, t, TicketStatus.Open), // not routed to a branch yet
            Made(company, _world.CliX, At(year - 1, 3, 10), TicketStatus.Open, _world.BranchB) // an earlier year: not in the period
        };
        await SaveAsync(tickets.Cast<object>().ToArray());
        await SaveAsync(
            Session(tickets[0], _world.EmpA1, At(year, 3, 11), 90), // staff of A, in the sub-branch
            Session(tickets[3], _world.EmpB1, At(year, 3, 11), 30), // staff of B
            Session(tickets[4], _world.Admin, At(year, 3, 11), 60), // belongs to no branch
            Session(tickets[0], _world.EmpA1, At(year, 5, 1), 600)); // outside the period
        return (company, sub);
    }

    private async Task WithEmpA1InAsync(SubBranch sub, Func<Task> test)
    {
        await _world.Db(db => db.Users.Where(u => u.Id == _world.EmpA1.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.SubBranchId, sub.Id)));
        try { await test(); }
        finally { await _world.Db(db => db.Users.Where(u => u.Id == _world.EmpA1.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.SubBranchId, (Guid?)null))); }
    }

    private static string March(int year) => $"from={year}-03-01T00:00:00Z&to={year}-03-31T23:59:59Z";

    [SqlFact]
    public async Task The_branches_are_counted_by_tickets_closed_hours_of_their_own_staff_and_share_with_their_sub_branches_spelled_out()
    {
        const int year = 2034;
        var (company, sub) = await BranchSceneAsync(year);

        await WithEmpA1InAsync(sub, async () =>
        {
            var r = await GetAsync(_world.Admin, $"{Base}/branch-load?companyId={company.Id}&{March(year)}");

            Assert.Equal(5, r.GetProperty("totalTickets").GetInt32());
            Assert.Equal(3.0, r.GetProperty("totalHours").GetDouble()); // 90 + 30 + 60 minutes
            var items = r.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(new[] { "Branch A", "", "Branch B" }, items.Select(i => i.GetProperty("name").GetString())); // 3 tickets, then the two with 1 by their hours: unrouted 1.0 h, B 0.5 h
            Assert.Equal(5, items.Sum(i => i.GetProperty("tickets").GetInt32())); // the rows add up to the total
            Assert.Equal(100.0, items.Sum(i => i.GetProperty("sharePercent").GetDouble()));

            var a = items[0];
            Assert.Equal((3, 1, 1.5, 60.0), (a.GetProperty("tickets").GetInt32(), a.GetProperty("closed").GetInt32(), a.GetProperty("hours").GetDouble(), a.GetProperty("sharePercent").GetDouble()));
            var subs = a.GetProperty("subBranches").EnumerateArray().ToList();
            Assert.Equal(2, subs.Count);
            Assert.Equal((sub.Name, 2, 1, 1.5), (subs[0].GetProperty("name").GetString(), subs[0].GetProperty("tickets").GetInt32(), subs[0].GetProperty("closed").GetInt32(), subs[0].GetProperty("hours").GetDouble()));
            Assert.Equal(JsonValueKind.Null, subs[1].GetProperty("subBranchId").ValueKind); // in none of its sub-branches
            Assert.Equal(1, subs[1].GetProperty("tickets").GetInt32());
            Assert.Equal(a.GetProperty("tickets").GetInt32(), subs.Sum(x => x.GetProperty("tickets").GetInt32())); // the sub-branches add up to the branch

            var unrouted = items[1];
            Assert.Equal(JsonValueKind.Null, unrouted.GetProperty("departmentId").ValueKind);
            Assert.Equal((1, 0, 1.0), (unrouted.GetProperty("tickets").GetInt32(), unrouted.GetProperty("closed").GetInt32(), unrouted.GetProperty("hours").GetDouble()));
            var b = items[2];
            Assert.Equal((1, 1, 0.5), (b.GetProperty("tickets").GetInt32(), b.GetProperty("closed").GetInt32(), b.GetProperty("hours").GetDouble()));
            Assert.Equal(0, b.GetProperty("subBranches").GetArrayLength()); // a branch with no sub-branches has its figures as a whole
        });
    }

    [SqlFact]
    public async Task The_hours_are_those_of_the_staff_of_the_branch_not_of_the_branch_the_ticket_is_in()
    {
        const int year = 2035;
        var company = await CompanyAsync("Premjestaj");
        var moved = Made(company, _world.CliX, At(year, 3, 10), TicketStatus.Open, _world.BranchB); // it is in B now, but A worked on it
        await SaveAsync(moved);
        await SaveAsync(Session(moved, _world.EmpA1, At(year, 3, 11), 120));

        var r = await GetAsync(_world.Admin, $"{Base}/branch-load?companyId={company.Id}&{March(year)}");

        var items = r.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!);
        Assert.Equal(1, items["Branch B"].GetProperty("tickets").GetInt32());
        Assert.Equal(0.0, items["Branch B"].GetProperty("hours").GetDouble());
        Assert.Equal(0, items["Branch A"].GetProperty("tickets").GetInt32());
        Assert.Equal(2.0, items["Branch A"].GetProperty("hours").GetDouble());
    }

    [SqlFact]
    public async Task An_employee_gets_the_figures_of_all_branches_like_Admin_and_a_client_none()
    {
        const int year = 2036;
        var (company, _) = await BranchSceneAsync(year);

        var admin = await GetAsync(_world.Admin, $"{Base}/branch-load?companyId={company.Id}&{March(year)}");
        var employee = await GetAsync(_world.EmpB1, $"{Base}/branch-load?companyId={company.Id}&{March(year)}");
        var cardAdmin = await GetAsync(_world.Admin, $"{Base}/card?companyId={company.Id}");
        var cardEmployee = await GetAsync(_world.EmpB1, $"{Base}/card?companyId={company.Id}");

        Assert.Equal(admin.GetRawText().Replace(admin.GetProperty("totalHours").GetRawText(), ""), employee.GetRawText().Replace(employee.GetProperty("totalHours").GetRawText(), ""));
        Assert.Equal(admin.GetProperty("totalHours").GetDouble(), employee.GetProperty("totalHours").GetDouble());
        Assert.Equal(cardAdmin.GetRawText(), cardEmployee.GetRawText());
        await GetAsync(_world.CliX, $"{Base}/card?companyId={company.Id}", 403);
        await GetAsync(_world.CliX, $"{Base}/branch-load?companyId={company.Id}", 403);
    }

    [SqlFact]
    public async Task Both_need_a_company_that_exists()
    {
        foreach (var route in new[] { "card", "branch-load" })
        {
            var none = await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/{route}");
            Assert.Equal(400, none.Code);
            Assert.Contains("Choose a company first.", none.Text);
            await GetAsync(_world.Admin, $"{Base}/{route}?companyId={Guid.NewGuid()}", 404);
            Assert.Equal(400, (await _world.Send(_world.Admin, HttpMethod.Get, $"{Base}/{route}?companyId={_world.CompanyId}&from=2036-05-01T00:00:00Z&to=2036-04-01T00:00:00Z")).Code);
        }
    }
}
