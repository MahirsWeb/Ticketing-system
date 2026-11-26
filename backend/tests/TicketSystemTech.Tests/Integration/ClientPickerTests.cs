using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The form for a new ticket picks a company and then a person of it (and nothing else can be chosen). Two things hold it up on the server:
/// the list of a company's clients that the pickers use (the same for the staff of every branch, with only what the picker needs), and the check, when the
/// ticket is made, that the client really exists, is switched on and belongs to a company that is switched on. The old list of clients stays isolated.
/// </summary>
[Collection(ApiCollection.Name)]
public class ClientPickerTests : IAsyncLifetime
{
    private const string Marker = "CP-";

    private readonly ApiWorld _world;
    private Guid _topic, _plan;

    public ClientPickerTests(ApiWorld world) => _world = world;

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        var topic = new HelpTopic { Name = "CP " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "CP " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db => { db.AddRange(topic, plan); return await db.SaveChangesAsync(); });
        (_topic, _plan) = (topic.Id, plan.Id);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            var tickets = await db.Tickets.Where(t => t.Title.StartsWith(Marker)).Select(t => t.Id).ToListAsync();
            await db.Notifications.Where(n => n.TicketId != null && tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.EmailOutbox.Where(m => m.Subject.Contains(Marker)).ExecuteDeleteAsync();
            await db.TicketMessages.Where(m => tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => h.Id == _topic).ExecuteDeleteAsync();
            return await db.SlaPlans.Where(p => p.Id == _plan).ExecuteDeleteAsync();
        });
    }

    private async Task<List<JsonElement>> CompanyClientsAsync(Actor? who, Guid company)
    {
        var response = await _world.Send(who, HttpMethod.Get, $"/api/users/company-clients?companyId={company}");
        Assert.Equal(200, response.Code);
        return JsonDocument.Parse(response.Text).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static HashSet<Guid> Ids(IEnumerable<JsonElement> items) => items.Select(e => e.GetProperty("id").GetGuid()).ToHashSet();

    // ============================ the list the pickers use ============================

    [SqlFact]
    public async Task Staff_of_any_branch_get_all_active_clients_of_the_company_including_those_who_never_wrote_to_their_branch()
    {
        var a = Ids(await CompanyClientsAsync(_world.EmpA1, _world.CompanyId));
        var b = Ids(await CompanyClientsAsync(_world.EmpB1, _world.CompanyId));
        var admin = Ids(await CompanyClientsAsync(_world.Admin, _world.CompanyId));

        Assert.Contains(_world.CliX.Id, a);
        Assert.Contains(_world.CliY.Id, a); // has tickets only in branch B
        Assert.Contains(_world.CliZ.Id, a); // has tickets nowhere: the first ticket of a new client is the reason this list exists
        Assert.Equal(a, b);
        Assert.Equal(a, admin);
    }

    [SqlFact]
    public async Task The_old_list_of_clients_stays_isolated_by_branch_the_new_one_is_the_only_exception()
    {
        var response = await _world.Send(_world.EmpA1, HttpMethod.Get, "/api/users?role=Client");
        var seen = JsonDocument.Parse(response.Text).RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToHashSet();

        Assert.Contains(_world.CliX.Id, seen);
        Assert.DoesNotContain(_world.CliY.Id, seen);
        Assert.DoesNotContain(_world.CliZ.Id, seen);
    }

    [SqlFact]
    public async Task Each_client_comes_with_the_name_and_the_address_and_nothing_else()
    {
        var items = await CompanyClientsAsync(_world.EmpA1, _world.CompanyId);
        var one = items.First(e => e.GetProperty("id").GetGuid() == _world.CliX.Id);

        Assert.Equal(new[] { "email", "id", "name" }, one.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(_world.CliX.Email, one.GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(one.GetProperty("name").GetString()));
    }

    [SqlFact]
    public async Task A_client_of_another_company_and_a_switched_off_client_are_not_on_the_list()
    {
        var other = new Company { Name = "CP other " + Guid.NewGuid().ToString("N")[..6] };
        await _world.Db(async db => { db.Companies.Add(other); return await db.SaveChangesAsync(); });
        try
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, other.Id)));
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliY.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

            var main = Ids(await CompanyClientsAsync(_world.EmpA1, _world.CompanyId));
            var elsewhere = Ids(await CompanyClientsAsync(_world.EmpA1, other.Id));

            Assert.DoesNotContain(_world.CliZ.Id, main); // moved to the other company
            Assert.DoesNotContain(_world.CliY.Id, main); // switched off
            Assert.Contains(_world.CliX.Id, main);
            Assert.Equal(new[] { _world.CliZ.Id }, elsewhere);
        }
        finally
        {
            await _world.Db(async db =>
            {
                await db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, _world.CompanyId));
                await db.Users.Where(u => u.Id == _world.CliY.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
                return await db.Companies.Where(c => c.Id == other.Id).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task A_client_cannot_ask_and_nobody_signed_out_can_either()
    {
        Assert.Equal(403, (await _world.Send(_world.CliX, HttpMethod.Get, $"/api/users/company-clients?companyId={_world.CompanyId}")).Code);
        Assert.Equal(401, (await _world.Send(null, HttpMethod.Get, $"/api/users/company-clients?companyId={_world.CompanyId}")).Code);
    }

    [SqlFact]
    public async Task A_company_that_does_not_exist_or_is_switched_off_or_is_not_named_has_no_list()
    {
        var unknown = await _world.Send(_world.EmpA1, HttpMethod.Get, $"/api/users/company-clients?companyId={Guid.NewGuid()}");
        var notNamed = await _world.Send(_world.EmpA1, HttpMethod.Get, "/api/users/company-clients");

        Assert.Equal(404, unknown.Code);
        Assert.Equal(404, notNamed.Code);

        await _world.Db(db => db.Companies.Where(c => c.Id == _world.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)));
        try
        {
            Assert.Equal(404, (await _world.Send(_world.EmpA1, HttpMethod.Get, $"/api/users/company-clients?companyId={_world.CompanyId}")).Code);
        }
        finally
        {
            await _world.Db(db => db.Companies.Where(c => c.Id == _world.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, true)));
        }
    }

    // ============================ making the ticket ============================

    private Task<ApiResponse> CreateAsync(Guid clientId, string title) =>
        _world.Send(_world.EmpA1, HttpMethod.Post, "/api/tickets/on-behalf", new
        {
            clientId,
            title = Marker + title,
            description = "<p>x</p>",
            source = "Phone",
            helpTopicId = _topic,
            departmentId = _world.BranchA,
            slaPlanId = _plan,
            priority = "Medium",
            assignedToUserId = _world.EmpA1.Id
        });

    [SqlFact]
    public async Task A_ticket_is_made_for_the_client_that_was_chosen_even_one_who_never_wrote_to_the_branch()
    {
        var created = await CreateAsync(_world.CliZ.Id, "za Z");

        Assert.Equal(200, created.Code);
        var id = JsonDocument.Parse(created.Text).RootElement.GetProperty("id").GetGuid();
        Assert.Equal(_world.CliZ.Id, await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.ClientId).SingleAsync()));
    }

    [SqlFact]
    public async Task Only_a_client_that_exists_can_be_chosen_not_an_unknown_id_and_not_a_member_of_staff()
    {
        var unknown = await CreateAsync(Guid.NewGuid(), "nepoznat");
        var staff = await CreateAsync(_world.EmpA2.Id, "osoblje");
        var empty = await CreateAsync(Guid.Empty, "prazan");

        Assert.Equal(404, unknown.Code);
        Assert.Equal(404, staff.Code);
        Assert.NotEqual(200, empty.Code);
        Assert.Contains("No client account found", unknown.Text);
        Assert.False(await _world.Db(db => db.Tickets.AnyAsync(t => t.Title.StartsWith(Marker))));
    }

    [SqlFact]
    public async Task A_switched_off_client_a_client_without_a_company_and_a_client_of_a_switched_off_company_are_refused()
    {
        try
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
            var off = await CreateAsync(_world.CliZ.Id, "ugasen");
            Assert.Equal(400, off.Code);
            Assert.Contains("not active", off.Text);
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true)));

            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, (Guid?)null)));
            var none = await CreateAsync(_world.CliZ.Id, "bez firme");
            Assert.Equal(400, none.Code);
            Assert.Contains("not linked to a company", none.Text);
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, _world.CompanyId)));

            await _world.Db(db => db.Companies.Where(c => c.Id == _world.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)));
            var company = await CreateAsync(_world.CliZ.Id, "ugasena firma");
            Assert.Equal(400, company.Code);
            Assert.Contains("company is not active", company.Text);
        }
        finally
        {
            await _world.Db(async db =>
            {
                await db.Companies.Where(c => c.Id == _world.CompanyId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, true));
                return await db.Users.Where(u => u.Id == _world.CliZ.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true).SetProperty(u => u.CompanyId, _world.CompanyId));
            });
        }

        Assert.False(await _world.Db(db => db.Tickets.AnyAsync(t => t.Title.StartsWith(Marker))));
    }
}
