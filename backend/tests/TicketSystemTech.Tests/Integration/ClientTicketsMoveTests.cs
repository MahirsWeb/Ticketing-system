using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// A ticket keeps the company it was written under, so putting a client into another company moves only the person. The administrator can say that
/// what the person wrote before should follow (for people who were in a temporary group); these are the rules of that.
/// </summary>
[Collection(ApiCollection.Name)]
public class ClientTicketsMoveTests
{
    private readonly ApiWorld _world;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];
    private readonly List<Guid> _people = [];
    private readonly List<Guid> _companies = [];
    private readonly List<Guid> _tickets = [];

    public ClientTicketsMoveTests(ApiWorld world) => _world = world;

    private async Task<Guid> Company(bool aiIndexing)
    {
        var id = await _world.Db(async db =>
        {
            var company = new Company { Name = $"Move {_tag} {_companies.Count}", IsActive = true, AiIndexingEnabled = aiIndexing };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            return company.Id;
        });
        _companies.Add(id);
        return id;
    }

    private async Task<(Actor Client, Guid[] Tickets)> ClientWithTickets(string who, Guid company, int tickets)
    {
        var client = await _world.CreateActorAsync($"mv{_tag}{who}", UserRole.Client, null, company);
        _people.Add(client.Id);
        var ids = await _world.Db(async db =>
        {
            var made = Enumerable.Range(1, tickets).Select(n => new Ticket
            {
                TicketNumber = $"MV{_tag}{who}{n}",
                Title = "Naslov",
                Description = "<p>Opis</p>",
                ClientId = client.Id,
                CompanyId = company,
                Status = TicketStatus.Open,
                DepartmentId = _world.BranchA,
            }).ToList();
            db.Tickets.AddRange(made);
            await db.SaveChangesAsync();
            return made.Select(t => t.Id).ToArray();
        });
        _tickets.AddRange(ids);
        return (client, ids);
    }

    private Task<ApiResponse> Edit(Actor client, Guid company, bool move, Actor? by = null) =>
        _world.Send(by ?? _world.Admin, HttpMethod.Put, $"/api/users/{client.Id}", new { firstName = "Ana", lastName = "Anić", role = "Client", companyId = company, moveTickets = move });

    private Task<List<Guid>> CompaniesOf(IEnumerable<Guid> tickets) =>
        _world.Db(db => db.Tickets.AsNoTracking().Where(t => tickets.Contains(t.Id)).Select(t => t.CompanyId).ToListAsync());

    private async Task CleanUp()
    {
        await _world.Db(async db =>
        {
            await db.KnowledgeBaseChunks.Where(k => k.TicketId != null && _tickets.Contains(k.TicketId.Value)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(a => a.EntityId != null && _people.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
            db.Tickets.RemoveRange(db.Tickets.Where(t => _tickets.Contains(t.Id)));
            await db.SaveChangesAsync();
            await db.Users.Where(u => _people.Contains(u.Id)).ExecuteDeleteAsync();
            await db.Companies.Where(c => _companies.Contains(c.Id)).ExecuteDeleteAsync();
            return 0;
        });
    }

    private static void Ok(ApiResponse response) => Assert.True(response.Code is 200 or 204, response.Text);

    [SqlFact]
    public async Task With_the_box_ticked_the_tickets_of_the_person_follow_them_and_only_theirs()
    {
        try
        {
            var from = await Company(aiIndexing: true);
            var to = await Company(aiIndexing: true);
            var (ana, anasTickets) = await ClientWithTickets("a", from, 2);
            var (_, dinosTickets) = await ClientWithTickets("d", from, 1);

            Ok(await Edit(ana, to, move: true));

            Assert.All(await CompaniesOf(anasTickets), c => Assert.Equal(to, c));
            Assert.All(await CompaniesOf(dinosTickets), c => Assert.Equal(from, c)); // somebody else's ticket of the same company stays
            Assert.Equal(to, await _world.Db(db => db.Users.AsNoTracking().Where(u => u.Id == ana.Id).Select(u => u.CompanyId).SingleAsync()));
            Assert.Equal(1, await _world.Db(db => db.AuditLogs.CountAsync(a => a.Action == "ClientTicketsMoved" && a.EntityId == ana.Id && a.Details!.Contains("tickets: 2"))));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Without_the_box_only_the_person_moves_as_it_always_did()
    {
        try
        {
            var from = await Company(aiIndexing: true);
            var to = await Company(aiIndexing: true);
            var (ana, anasTickets) = await ClientWithTickets("a", from, 2);

            Ok(await Edit(ana, to, move: false));

            Assert.All(await CompaniesOf(anasTickets), c => Assert.Equal(from, c));
            Assert.Equal(to, await _world.Db(db => db.Users.AsNoTracking().Where(u => u.Id == ana.Id).Select(u => u.CompanyId).SingleAsync()));
            Assert.Equal(0, await _world.Db(db => db.AuditLogs.CountAsync(a => a.Action == "ClientTicketsMoved" && a.EntityId == ana.Id)));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Asking_again_with_the_box_ticked_finishes_a_move_and_asking_a_third_time_changes_nothing()
    {
        try
        {
            var from = await Company(aiIndexing: true);
            var to = await Company(aiIndexing: true);
            var (ana, anasTickets) = await ClientWithTickets("a", from, 2);
            Ok(await Edit(ana, to, move: false)); // the person is in the new company, the tickets are not

            Ok(await Edit(ana, to, move: true));
            Assert.All(await CompaniesOf(anasTickets), c => Assert.Equal(to, c));
            Ok(await Edit(ana, to, move: true));

            Assert.Equal(1, await _world.Db(db => db.AuditLogs.CountAsync(a => a.Action == "ClientTicketsMoved" && a.EntityId == ana.Id)));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_company_that_keeps_its_tickets_from_the_outside_AI_keeps_nothing_of_the_moved_ones_indexed()
    {
        try
        {
            var from = await Company(aiIndexing: true);
            var closedCompany = await Company(aiIndexing: false);
            var openCompany = await Company(aiIndexing: true);
            var (ana, anasTickets) = await ClientWithTickets("a", from, 1);
            var (dino, dinosTickets) = await ClientWithTickets("d", from, 1);
            await _world.Db(async db =>
            {
                db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk { TicketId = anasTickets[0], Content = "ana" });
                db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk { TicketId = dinosTickets[0], Content = "dino" });
                await db.SaveChangesAsync();
                return 0;
            });

            Ok(await Edit(ana, closedCompany, move: true));
            Ok(await Edit(dino, openCompany, move: true));

            var left = await _world.Db(db => db.KnowledgeBaseChunks.AsNoTracking().Where(k => k.TicketId == anasTickets[0] || k.TicketId == dinosTickets[0]).Select(k => k.Content).ToListAsync());
            Assert.Equal(["dino"], left); // the one that went to a company that does not allow it is gone, the other stays
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Only_an_administrator_can_ask_for_it()
    {
        try
        {
            var from = await Company(aiIndexing: true);
            var to = await Company(aiIndexing: true);
            var (ana, anasTickets) = await ClientWithTickets("a", from, 1);

            var refused = await Edit(ana, to, move: true, by: _world.EmpA1);

            Assert.Equal(403, refused.Code);
            Assert.All(await CompaniesOf(anasTickets), c => Assert.Equal(from, c));
        }
        finally { await CleanUp(); }
    }
}
