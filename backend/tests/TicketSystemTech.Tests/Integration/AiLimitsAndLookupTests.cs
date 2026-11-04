using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>T-26 (limits on AI use), T-56 (lookups in use cannot be deleted) and T-58 (ticket numbers sort as numbers).</summary>
[Collection(ApiCollection.Name)]
public class AiLimitsAndLookupTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _topics = new();
    private readonly List<Guid> _categories = new();
    private readonly List<Guid> _plans = new();

    public AiLimitsAndLookupTests(ApiWorld world) => _world = world;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _world.Db(async db =>
        {
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(x => _topics.Contains(x.Id)).ExecuteDeleteAsync();
            await db.TicketCategories.Where(x => _categories.Contains(x.Id)).ExecuteDeleteAsync();
            return await db.SlaPlans.Where(x => _plans.Contains(x.Id)).ExecuteDeleteAsync();
        });
    }

    // ======================= how much may be asked =======================

    private static string Text(int length) => new('q', length);

    [SqlFact]
    public async Task A_question_over_the_limit_is_refused_and_one_at_the_limit_is_answered()
    {
        var tooLong = Text(AiPromptLimits.MaxQuestionChars + 1);

        Assert.Equal(400, (await _world.Post(_world.EmpA1, "/api/knowledge-base/ask", new { question = tooLong })).Code);
        Assert.Equal(400, (await _world.Post(_world.CliX, "/api/knowledge-base/ask-client", new { question = tooLong })).Code);
        Assert.Equal(400, (await _world.Get(_world.EmpA1, $"/api/knowledge-base/search?query={tooLong}")).Code);
        Assert.Equal(400, (await _world.Get(_world.EmpA1, $"/api/knowledge-base/similar-tickets?query={tooLong}")).Code);

        Assert.Equal(200, (await _world.Post(_world.EmpA1, "/api/knowledge-base/ask", new { question = Text(AiPromptLimits.MaxQuestionChars) })).Code);
    }

    [SqlFact]
    public async Task What_the_AI_is_given_as_context_stays_under_the_ceiling()
    {
        var response = await _world.Post(_world.Admin, "/api/knowledge-base/ask", new { question = "zebra" });

        Assert.Equal(200, response.Code);
        Assert.True(_world.Factory.Chat.LastContext!.Length <= AiPromptLimits.MaxContextChars);
    }

    [SqlFact]
    public async Task The_staff_AI_allowance_is_per_person_so_one_heavy_user_does_not_use_up_a_colleagues()
    {
        var heavy = await _world.CreateActorAsync("heavyai", UserRole.Employee, _world.BranchA, null);

        // 60 in ten minutes are allowed (RateLimits:AiStaffPerTenMinutes), the 61st is refused
        for (var i = 0; i < 60; i++)
            Assert.Equal(200, (await _world.Get(heavy, "/api/knowledge-base/search?query=zebra")).Code);
        var refused = await _world.Get(heavy, "/api/knowledge-base/search?query=zebra");

        Assert.Equal(429, refused.Code);

        // the same address, a different person: not affected
        Assert.Equal(200, (await _world.Get(_world.EmpA2, "/api/knowledge-base/search?query=zebra")).Code);
        // and the other AI endpoints of the same person share the allowance
        Assert.Equal(429, (await _world.Post(heavy, "/api/knowledge-base/ask", new { question = "zebra" })).Code);
    }

    // ======================= lookups that tickets use =======================

    private async Task<(Guid Topic, Guid Category, Guid Plan, Guid Ticket)> LookupsInUseAsync()
    {
        var topic = new HelpTopic { Name = "In use " + Guid.NewGuid().ToString("N")[..6] };
        var category = new TicketCategory { Name = "In use " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "In use " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        var ticket = new Ticket
        {
            TicketNumber = "LK-" + Guid.NewGuid().ToString("N")[..8],
            Title = "lookups",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            HelpTopicId = topic.Id,
            CategoryId = category.Id,
            SlaPlanId = plan.Id,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };

        await _world.Db(async db =>
        {
            db.AddRange(topic, category, plan);
            await db.SaveChangesAsync();
            db.Tickets.Add(ticket);
            return await db.SaveChangesAsync();
        });
        _topics.Add(topic.Id);
        _categories.Add(category.Id);
        _plans.Add(plan.Id);
        _tickets.Add(ticket.Id);
        return (topic.Id, category.Id, plan.Id, ticket.Id);
    }

    [SqlFact]
    public async Task A_help_topic_category_or_sla_plan_that_a_ticket_uses_cannot_be_deleted()
    {
        var (topic, category, plan, ticket) = await LookupsInUseAsync();

        var topicResponse = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/help-topics/{topic}");
        var categoryResponse = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/ticket-categories/{category}");
        var planResponse = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/sla-plans/{plan}");

        foreach (var response in new[] { topicResponse, categoryResponse, planResponse })
        {
            Assert.Equal(400, response.Code);
            Assert.Contains("Deactivate it instead", response.Text);
        }

        // nothing was removed and the ticket still has all three
        var kept = await _world.Db(db => db.Tickets.AsNoTracking().Where(t => t.Id == ticket).Select(t => new { t.HelpTopicId, t.CategoryId, t.SlaPlanId }).SingleAsync());
        Assert.Equal((topic, category, plan), (kept.HelpTopicId, kept.CategoryId, kept.SlaPlanId));
    }

    [SqlFact]
    public async Task The_database_itself_also_refuses_it()
    {
        // The check above is in the application; the foreign key is what protects the history from every other way in.
        var (topic, _, _, _) = await LookupsInUseAsync();

        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            _world.Db(db => db.HelpTopics.Where(x => x.Id == topic).ExecuteDeleteAsync()));
        Assert.Contains("FK_Tickets_HelpTopics_HelpTopicId", error.Message);
    }

    [SqlFact]
    public async Task Once_no_ticket_uses_them_they_can_be_deleted()
    {
        var (topic, category, plan, ticket) = await LookupsInUseAsync();
        await _world.Db(db => db.Tickets.Where(t => t.Id == ticket).ExecuteDeleteAsync());

        Assert.Equal(204, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/help-topics/{topic}")).Code);
        Assert.Equal(204, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/ticket-categories/{category}")).Code);
        Assert.Equal(204, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/sla-plans/{plan}")).Code);
    }

    // ======================= ticket numbers =======================

    [SqlFact]
    public async Task Ticket_numbers_sort_as_numbers_not_as_text()
    {
        var prefix = "SRT" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant() + "-";
        await _world.Db(async db =>
        {
            foreach (var number in new[] { "1000", "99", "100", "5" })
            {
                var ticket = new Ticket
                {
                    TicketNumber = prefix + number,
                    Title = "sort",
                    Description = "<p>x</p>",
                    ClientId = _world.CliX.Id,
                    CompanyId = _world.CompanyId,
                    Status = TicketStatus.InProgress,
                    DepartmentId = _world.BranchA,
                    Priority = TicketPriority.Medium,
                    Source = TicketSource.TicketSystem
                };
                db.Tickets.Add(ticket);
                _tickets.Add(ticket.Id);
            }
            return await db.SaveChangesAsync();
        });

        var ascending = await _world.Get(_world.Admin, $"/api/tickets?search={prefix}&sortBy=ticketNumber&sortDir=asc&pageSize=50");
        var descending = await _world.Get(_world.Admin, $"/api/tickets?search={prefix}&sortBy=ticketNumber&sortDir=desc&pageSize=50");

        Assert.Equal(new[] { "5", "99", "100", "1000" }.Select(n => prefix + n), ascending.Values("ticketNumber", "items"));
        Assert.Equal(new[] { "1000", "100", "99", "5" }.Select(n => prefix + n), descending.Values("ticketNumber", "items"));
    }
}
