using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Another application (one that records working time) reads tickets by an API key: who was put on a ticket since it last asked, and which
/// tickets a person is on now, found by the e-mail address both applications know the person by. The tests make their own people and tickets
/// (with times in 2031, so that nothing else in the shared world can be mistaken for them) and remove them again.
/// </summary>
[Collection(ApiCollection.Name)]
public class IntegrationApiTests
{
    private static readonly IDictionary<string, string> Key = new Dictionary<string, string> { ["X-Api-Key"] = ApiWorld.IntegrationKey };
    private static readonly DateTime Start = new(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string StartText = "2031-01-01T00:00:00Z";

    private readonly ApiWorld _world;
    private readonly List<Guid> _made = [];

    public IntegrationApiTests(ApiWorld world) => _world = world;

    // ======================= what the tests need =======================

    private Task<Actor> Staff(string tag, UserRole role = UserRole.Employee, Guid? branch = null) =>
        _world.CreateActorAsync(tag + Guid.NewGuid().ToString("N")[..6], role, branch ?? _world.BranchA, null);

    private async Task<Guid> Ticket(string number, TicketStatus status, DateTime createdAt, Guid? branch = null, params (Actor Who, DateTime At, bool Primary)[] on)
    {
        var closed = status == TicketStatus.Closed;
        var ticket = new Ticket
        {
            TicketNumber = number,
            Title = "Title " + number,
            Description = "<p>Description " + number + "</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = branch ?? _world.BranchA,
            Priority = TicketPriority.High,
            Source = TicketSource.TicketSystem,
            CreatedAt = createdAt,
            OpenedAtUtc = createdAt,
            ClosedAtUtc = closed ? createdAt.AddHours(1) : null,
            ResolutionSummary = closed ? "Fixed " + number : null
        };
        await _world.Db(async db =>
        {
            db.Tickets.Add(ticket);
            foreach (var (who, at, primary) in on)
                db.TicketAssignees.Add(new TicketAssignee { TicketId = ticket.Id, UserId = who.Id, AssignedAtUtc = at, IsPrimary = primary });
            return await db.SaveChangesAsync();
        });
        _made.Add(ticket.Id);
        return ticket.Id;
    }

    private async Task CleanUp()
    {
        var ids = _made.ToList();
        var audit = _audit.ToList();
        var tasks = _tasks.ToList();
        await _world.Db(async db =>
        {
            // What the application itself writes when a test drives it (messages, notices, the trail) is removed before the tickets they point at.
            await db.TicketMessages.Where(m => ids.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && ids.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(a => audit.Contains(a.Id) || (a.EntityId != null && ids.Contains(a.EntityId.Value))).ExecuteDeleteAsync();
            await db.WorkTaskSessions.Where(s => tasks.Contains(s.WorkTaskId)).ExecuteDeleteAsync();
            await db.WorkTaskAssignmentLogs.Where(l => tasks.Contains(l.WorkTaskId)).ExecuteDeleteAsync();
            await db.WorkTasks.Where(t => tasks.Contains(t.Id)).ExecuteDeleteAsync();
            db.Tickets.RemoveRange(db.Tickets.Where(t => ids.Contains(t.Id))); // the assignees and the work sessions go with their tickets
            return await db.SaveChangesAsync();
        });
    }

    private readonly List<Guid> _audit = [];
    private readonly List<Guid> _tasks = [];

    private async Task<Guid> Session(Guid ticket, Actor who, DateTime started, DateTime? ended)
    {
        var session = new TicketWorkSession { TicketId = ticket, UserId = who.Id, StartedAtUtc = started, EndedAtUtc = ended };
        await _world.Db(async db => { db.TicketWorkSessions.Add(session); return await db.SaveChangesAsync(); });
        return session.Id;
    }

    /// <summary>A task (work with no ticket and no client) with one stretch of work on it.</summary>
    private async Task<(Guid Task, Guid Session)> TaskSession(Actor who, string title, DateTime started, DateTime? ended)
    {
        var task = new WorkTask { Title = title, Description = "", DepartmentId = _world.BranchA, CreatedByUserId = who.Id, AssignedByUserId = who.Id, AssignedToUserId = who.Id };
        var session = new WorkTaskSession { WorkTaskId = task.Id, UserId = who.Id, StartedAtUtc = started, EndedAtUtc = ended };
        await _world.Db(async db => { db.WorkTasks.Add(task); db.WorkTaskSessions.Add(session); return await db.SaveChangesAsync(); });
        _tasks.Add(task.Id);
        return (task.Id, session.Id);
    }

    private Task<ApiResponse> Time(string query, string? key = ApiWorld.IntegrationKey) =>
        _world.Get(null, "/api/integration/time-entries" + query, key is null ? null : new Dictionary<string, string> { ["X-Api-Key"] = key });

    /// <summary>A row of the audit trail of a ticket, as the application would have written it, at a time of the test's choosing.</summary>
    private async Task Trail(Guid ticket, string action, string? details, DateTime at, string entityType = "Ticket")
    {
        var row = new AuditLog { Action = action, EntityType = entityType, EntityId = ticket, Details = details, CreatedAt = at };
        await _world.Db(async db => { db.AuditLogs.Add(row); return await db.SaveChangesAsync(); });
        _audit.Add(row.Id);
    }

    private Task<ApiResponse> Events(string query, string? key = ApiWorld.IntegrationKey) =>
        _world.Get(null, "/api/integration/events" + query, key is null ? null : new Dictionary<string, string> { ["X-Api-Key"] = key });

    private Task<ApiResponse> One(Guid id, string query = "", string? key = ApiWorld.IntegrationKey) =>
        _world.Get(null, $"/api/integration/tickets/{id}" + query, key is null ? null : new Dictionary<string, string> { ["X-Api-Key"] = key });

    private Task<ApiResponse> Feed(string query, string? key = ApiWorld.IntegrationKey) =>
        _world.Get(null, "/api/integration/assignments" + query, key is null ? null : new Dictionary<string, string> { ["X-Api-Key"] = key });

    private Task<ApiResponse> Picture(string query, string? key = ApiWorld.IntegrationKey) =>
        _world.Get(null, "/api/integration/tickets" + query, key is null ? null : new Dictionary<string, string> { ["X-Api-Key"] = key });

    private static string At(DateTime utc) => Uri.EscapeDataString(utc.ToString("o"));

    private static List<JsonElement> Items(ApiResponse response) => response.Json.GetProperty("items").EnumerateArray().ToList();

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).Order().ToArray();

    // ======================= who may ask =======================

    [SqlFact]
    public async Task A_request_without_a_valid_key_is_turned_away_with_401()
    {
        var url = "/api/integration/assignments?since=" + StartText;

        Assert.Equal(401, (await _world.Get(null, url)).Code);
        Assert.Equal(401, (await _world.Get(null, url, new Dictionary<string, string> { ["X-Api-Key"] = "tix_not-a-key" })).Code);
        Assert.Equal(401, (await _world.Get(null, url, new Dictionary<string, string> { ["X-Api-Key"] = "" })).Code);
        Assert.Equal(401, (await _world.Get(null, "/api/integration/tickets", new Dictionary<string, string> { ["X-Api-Key"] = "tix_not-a-key" })).Code);

        var refused = await _world.Get(null, url);
        Assert.Contains("X-Api-Key", refused.Text); // it says where the key goes
        Assert.Equal("ApiKey", refused.Header("WWW-Authenticate"));
    }

    [SqlFact]
    public async Task A_persons_token_does_not_open_the_integration_endpoints_not_even_the_administrators()
    {
        var url = "/api/integration/assignments?since=" + StartText;

        Assert.Equal(401, (await _world.Get(_world.Admin, url)).Code);
        Assert.Equal(401, (await _world.Get(_world.EmpA1, url)).Code);
        Assert.Equal(401, (await _world.Get(_world.Admin, "/api/integration/tickets")).Code);
    }

    [SqlFact]
    public async Task The_key_is_not_a_token_it_is_not_accepted_as_a_bearer_and_opens_nothing_outside_the_integration_endpoints()
    {
        var asBearer = new Dictionary<string, string> { ["Authorization"] = "Bearer " + ApiWorld.IntegrationKey };

        Assert.Equal(401, (await _world.Get(null, "/api/integration/tickets", asBearer)).Code);
        foreach (var url in new[] { "/api/tickets", "/api/notifications", "/api/users", "/api/reports/summary", "/api/departments" })
            Assert.Equal(401, (await _world.Get(null, url, Key)).Code);
    }

    [SqlFact]
    public async Task The_right_key_is_let_through_and_either_of_the_two_keys_will_do()
    {
        Assert.Equal(200, (await Feed("?since=" + StartText)).Code);
        Assert.Equal(200, (await Picture("")).Code);
        Assert.Equal(200, (await Feed("?since=" + StartText, ApiWorld.IntegrationKeyOther)).Code);
    }

    // ======================= the feed =======================

    [SqlFact]
    public async Task Since_is_required_and_has_to_be_a_time()
    {
        var missing = await Feed("");
        var nonsense = await Feed("?since=yesterday");

        Assert.Equal(400, missing.Code);
        Assert.Equal(400, nonsense.Code);
        Assert.Contains("since", missing.Text);
        Assert.Contains("since", nonsense.Text);
    }

    [SqlFact]
    public async Task The_feed_lists_who_was_put_on_which_ticket_oldest_first_with_the_addresses_both_applications_know()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ben, Start.AddMinutes(30), true));
            await Ticket("INT-2", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(10), true));
            await Ticket("INT-3", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(20), true));

            var response = await Feed("?since=" + StartText);

            Assert.Equal(200, response.Code);
            var items = Items(response);
            Assert.Equal(3, items.Count);
            Assert.Equal(new[] { "INT-2", "INT-3", "INT-1" }, items.Select(i => i.GetProperty("ticket").GetProperty("ticketNumber").GetString()));
            Assert.Equal(new[] { ana.Email, ana.Email, ben.Email }, items.Select(i => i.GetProperty("assignee").GetProperty("email").GetString()));
            Assert.Equal(new[] { Start.AddMinutes(10), Start.AddMinutes(20), Start.AddMinutes(30) }, items.Select(i => i.GetProperty("assignedAtUtc").GetDateTime().ToUniversalTime()));

            var first = items[0].GetProperty("ticket");
            Assert.Equal("Title INT-2", first.GetProperty("title").GetString());
            Assert.Equal("<p>Description INT-2</p>", first.GetProperty("description").GetString());
            Assert.Equal("Open", first.GetProperty("status").GetString());
            Assert.Equal("High", first.GetProperty("priority").GetString());
            Assert.Equal("Branch A", first.GetProperty("branch").GetString());
            Assert.Equal(_world.CliX.Email, first.GetProperty("client").GetProperty("email").GetString());
            Assert.EndsWith("/tickets/" + first.GetProperty("id").GetGuid(), first.GetProperty("url").GetString());
            Assert.True(items[0].GetProperty("isPrimary").GetBoolean());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_feed_can_be_asked_for_one_person_by_address_in_any_case()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(1), true));
            await Ticket("INT-2", TicketStatus.Open, Start, null, (ben, Start.AddMinutes(2), true));

            var exact = Items(await Feed($"?since={StartText}&email={Uri.EscapeDataString(ana.Email)}"));
            var capitals = Items(await Feed($"?since={StartText}&email={Uri.EscapeDataString(ana.Email.ToUpperInvariant())}"));
            var padded = Items(await Feed($"?since={StartText}&email={Uri.EscapeDataString("  " + ana.Email + "  ")}"));

            Assert.Equal(["INT-1"], exact.Select(i => i.GetProperty("ticket").GetProperty("ticketNumber").GetString()));
            Assert.Single(capitals);
            Assert.Single(padded);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task An_address_nobody_has_is_an_empty_answer_not_an_error_even_when_there_are_tickets_for_others()
    {
        var ana = await Staff("ana");
        try
        {
            // There IS something to return, so an answer that ignored the address would not be empty.
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(1), true));

            var feed = await Feed($"?since={StartText}&email=nobody.has.this@authz.test");
            var picture = await Picture("?assigneeEmail=nobody.has.this@authz.test&state=all");

            Assert.Equal(200, feed.Code);
            Assert.Empty(Items(feed));
            Assert.False(feed.Json.GetProperty("hasMore").GetBoolean());
            Assert.Equal(200, picture.Code);
            Assert.Empty(Items(picture));
            Assert.Equal(0, picture.Json.GetProperty("totalCount").GetInt32());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Everybody_on_a_ticket_is_reported_each_as_an_assignment_and_only_one_of_them_is_primary()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ana, Start.AddMinutes(1), true), (ben, Start.AddMinutes(2), false));

            var items = Items(await Feed($"?since={StartText}"));

            Assert.Equal(2, items.Count);
            Assert.Equal([true, false], items.Select(i => i.GetProperty("isPrimary").GetBoolean()));
            var onTicket = items[0].GetProperty("ticket").GetProperty("assignees").EnumerateArray().ToList();
            Assert.Equal(new[] { ana.Email, ben.Email }, onTicket.Select(a => a.GetProperty("email").GetString()));
            Assert.Equal([true, false], onTicket.Select(a => a.GetProperty("isPrimary").GetBoolean()));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_person_taken_off_a_ticket_is_no_longer_in_the_feed_or_on_the_ticket()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            var id = await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ana, Start.AddMinutes(1), true), (ben, Start.AddMinutes(2), false));
            await _world.Db(async db =>
            {
                db.TicketAssignees.RemoveRange(db.TicketAssignees.Where(a => a.TicketId == id && a.UserId == ben.Id));
                return await db.SaveChangesAsync();
            });

            var items = Items(await Feed($"?since={StartText}"));

            Assert.Single(items);
            Assert.Equal([ana.Email], items[0].GetProperty("ticket").GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("email").GetString()));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Paging_through_assignments_made_in_the_same_instant_loses_none_and_repeats_none()
    {
        var ana = await Staff("ana");
        try
        {
            // A bulk change: five people-on-tickets with one and the same time, then one later. Time alone cannot say where a page ended.
            for (var n = 1; n <= 5; n++) await Ticket("INT-" + n, TicketStatus.Open, Start, null, (ana, Start.AddMinutes(5), true));
            await Ticket("INT-6", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(6), true));

            var seen = new List<string>();
            var since = StartText;
            string? after = null;
            for (var page = 0; page < 10; page++)
            {
                var response = await Feed($"?since={Uri.EscapeDataString(since)}&limit=2" + (after is null ? "" : "&afterId=" + after));
                Assert.Equal(200, response.Code);
                seen.AddRange(Items(response).Select(i => i.GetProperty("assignmentId").GetString()!));
                var json = response.Json;
                since = json.GetProperty("nextSince").GetDateTime().ToUniversalTime().ToString("o");
                after = json.GetProperty("nextAfterId").ValueKind == JsonValueKind.Null ? null : json.GetProperty("nextAfterId").GetString();
                if (!json.GetProperty("hasMore").GetBoolean()) break;
            }

            Assert.Equal(6, seen.Count);
            Assert.Equal(6, seen.Distinct().Count());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_page_says_whether_there_is_more_and_where_to_go_on_and_asking_again_with_nothing_new_gives_the_same_place()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(1), true));
            await Ticket("INT-2", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(2), true));
            await Ticket("INT-3", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(3), true));

            var first = (await Feed($"?since={StartText}&limit=2")).Json;
            Assert.Equal(2, first.GetProperty("items").GetArrayLength());
            Assert.True(first.GetProperty("hasMore").GetBoolean());

            var since = Uri.EscapeDataString(first.GetProperty("nextSince").GetDateTime().ToUniversalTime().ToString("o"));
            var after = first.GetProperty("nextAfterId").GetString();
            var second = (await Feed($"?since={since}&afterId={after}&limit=2")).Json;
            Assert.Equal(1, second.GetProperty("items").GetArrayLength());
            Assert.False(second.GetProperty("hasMore").GetBoolean());

            var since2 = Uri.EscapeDataString(second.GetProperty("nextSince").GetDateTime().ToUniversalTime().ToString("o"));
            var after2 = second.GetProperty("nextAfterId").GetString();
            var nothing = (await Feed($"?since={since2}&afterId={after2}&limit=2")).Json;
            Assert.Equal(0, nothing.GetProperty("items").GetArrayLength());
            Assert.False(nothing.GetProperty("hasMore").GetBoolean());
            Assert.Equal(second.GetProperty("nextSince").GetDateTime(), nothing.GetProperty("nextSince").GetDateTime());
            Assert.Equal(after2, nothing.GetProperty("nextAfterId").GetString());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Keeping_only_the_time_may_show_the_last_item_again_but_never_misses_one()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(1), true));
            await Ticket("INT-2", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(2), true));

            var items = Items(await Feed("?since=" + At(Start.AddMinutes(2))));

            Assert.Equal(["INT-2"], items.Select(i => i.GetProperty("ticket").GetProperty("ticketNumber").GetString()));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_limit_is_kept_between_one_and_five_hundred()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(1), true));
            await Ticket("INT-2", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(2), true));

            Assert.Single(Items(await Feed($"?since={StartText}&limit=0")));
            Assert.Single(Items(await Feed($"?since={StartText}&limit=-5")));
            Assert.Equal(2, Items(await Feed($"?since={StartText}&limit=100000")).Count);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_time_without_an_offset_is_read_as_utc()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start.AddMinutes(1), true));

            // 2031-01-01 00:00:30 with no offset must mean UTC, wherever the server is: the assignment is a minute after it.
            Assert.Single(Items(await Feed("?since=2031-01-01T00:00:30&email=" + Uri.EscapeDataString(ana.Email))));
            Assert.Empty(Items(await Feed("?since=2031-01-01T00:01:30&email=" + Uri.EscapeDataString(ana.Email))));
        }
        finally { await CleanUp(); }
    }

    // ======================= the picture =======================

    [SqlFact]
    public async Task The_picture_lists_the_tickets_a_person_is_on_now_and_only_those_being_worked_on_unless_asked_otherwise()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            await Ticket("INT-1", TicketStatus.InProgress, Start.AddMinutes(1), null, (ana, Start, true));
            await Ticket("INT-2", TicketStatus.Resolved, Start.AddMinutes(2), null, (ana, Start, true));
            await Ticket("INT-3", TicketStatus.Closed, Start.AddMinutes(3), null, (ana, Start, true));
            await Ticket("INT-4", TicketStatus.Open, Start.AddMinutes(4), null, (ben, Start, true));
            await Ticket("INT-5", TicketStatus.Open, Start.AddMinutes(5), null, (ben, Start, true), (ana, Start, false)); // she helps

            string[] Numbers(ApiResponse r) => Items(r).Select(t => t.GetProperty("ticketNumber").GetString()!).ToArray();
            var email = Uri.EscapeDataString(ana.Email);

            Assert.Equal(new[] { "INT-5", "INT-2", "INT-1" }, Numbers(await Picture($"?assigneeEmail={email}")));
            Assert.Equal(new[] { "INT-5", "INT-2", "INT-1" }, Numbers(await Picture($"?assigneeEmail={email}&state=active")));
            Assert.Equal(new[] { "INT-3" }, Numbers(await Picture($"?assigneeEmail={email}&state=closed")));
            Assert.Equal(new[] { "INT-5", "INT-3", "INT-2", "INT-1" }, Numbers(await Picture($"?assigneeEmail={email}&state=all")));
            Assert.Equal(new[] { "INT-5", "INT-2", "INT-1" }, Numbers(await Picture($"?assigneeEmail={Uri.EscapeDataString(ana.Email.ToUpperInvariant())}")));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_picture_shows_the_closed_ticket_with_its_resolution_and_the_time_it_was_closed()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-3", TicketStatus.Closed, Start, null, (ana, Start, true));

            var ticket = Items(await Picture($"?assigneeEmail={Uri.EscapeDataString(ana.Email)}&state=closed")).Single();

            Assert.Equal("Closed", ticket.GetProperty("status").GetString());
            Assert.Equal("Fixed INT-3", ticket.GetProperty("resolutionSummary").GetString());
            Assert.Equal(Start.AddHours(1), ticket.GetProperty("closedAtUtc").GetDateTime().ToUniversalTime());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_picture_is_paged_and_counts_all_of_them()
    {
        var ana = await Staff("ana");
        try
        {
            for (var n = 1; n <= 5; n++) await Ticket("INT-" + n, TicketStatus.Open, Start.AddMinutes(n), null, (ana, Start, true));
            var email = Uri.EscapeDataString(ana.Email);

            var second = (await Picture($"?assigneeEmail={email}&page=2&pageSize=2")).Json;

            Assert.Equal(5, second.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, second.GetProperty("page").GetInt32());
            Assert.Equal(2, second.GetProperty("pageSize").GetInt32());
            Assert.Equal(new[] { "INT-3", "INT-2" }, second.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("ticketNumber").GetString()));
            Assert.Equal(100, (await Picture($"?assigneeEmail={email}&pageSize=1000")).Json.GetProperty("pageSize").GetInt32()); // a page is at most 100
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_picture_without_an_address_lists_every_ticket_and_a_state_that_does_not_exist_is_refused()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.New, Start, null); // nobody on it yet

            var all = Items(await Picture("?state=all&pageSize=100")).Select(t => t.GetProperty("ticketNumber").GetString()).ToList();
            var refused = await Picture("?state=wishful");

            Assert.Contains("INT-1", all);
            Assert.Contains("AZ-A1", all); // the world's own tickets are there too: it reads every branch
            Assert.Equal(400, refused.Code);
            Assert.Contains("state", refused.Text);
            var withEmail = Items(await Picture($"?assigneeEmail={Uri.EscapeDataString(ana.Email)}&state=all"));
            Assert.Empty(withEmail);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_key_sees_every_branch_not_only_one()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-A", TicketStatus.Open, Start, _world.BranchA, (ana, Start, true));
            await Ticket("INT-B", TicketStatus.Open, Start.AddMinutes(1), _world.BranchB, (ana, Start.AddMinutes(1), true));

            var branches = Items(await Picture($"?assigneeEmail={Uri.EscapeDataString(ana.Email)}")).Select(t => t.GetProperty("branch").GetString());

            Assert.Equal(new[] { "Branch B", "Branch A" }, branches);
        }
        finally { await CleanUp(); }
    }

    // ======================= one ticket =======================

    [SqlFact]
    public async Task One_ticket_is_read_by_its_id_whatever_its_status_and_an_id_nobody_has_is_404()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            var closed = await Ticket("INT-1", TicketStatus.Closed, Start, null, (ana, Start, true), (ben, Start.AddMinutes(1), false));
            var response = await One(closed);

            Assert.Equal(200, response.Code);
            var ticket = response.Json;
            Assert.Equal(closed, ticket.GetProperty("id").GetGuid());
            Assert.Equal("INT-1", ticket.GetProperty("ticketNumber").GetString());
            Assert.Equal("Closed", ticket.GetProperty("status").GetString());
            Assert.Equal("Fixed INT-1", ticket.GetProperty("resolutionSummary").GetString());
            Assert.Equal([ana.Email, ben.Email], ticket.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("email").GetString()));

            var nobody = await One(Guid.NewGuid());
            Assert.Equal(404, nobody.Code);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_ticket_that_left_a_persons_list_can_be_read_to_see_whether_it_was_closed_or_she_was_taken_off()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            var closed = await Ticket("INT-1", TicketStatus.Closed, Start, null, (ana, Start, true));
            var shared = await Ticket("INT-2", TicketStatus.InProgress, Start.AddMinutes(1), null, (ana, Start, true), (ben, Start, false));
            await _world.Db(async db =>
            {
                db.TicketAssignees.RemoveRange(db.TicketAssignees.Where(a => a.TicketId == shared && a.UserId == ana.Id)); // she is taken off, the ticket goes on
                return await db.SaveChangesAsync();
            });
            var email = Uri.EscapeDataString(ana.Email);

            // Neither is in her list of tickets being worked on...
            Assert.Empty(Items(await Picture($"?assigneeEmail={email}")));
            // ...and one ticket each says what became of it.
            var wasClosed = (await One(closed)).Json;
            var tookOff = (await One(shared)).Json;
            Assert.Equal("Closed", wasClosed.GetProperty("status").GetString());
            Assert.Equal("InProgress", tookOff.GetProperty("status").GetString());
            Assert.Equal([ben.Email], tookOff.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("email").GetString()));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_new_calls_are_closed_to_everything_but_the_key()
    {
        foreach (var url in new[] { "/api/integration/events?since=" + StartText, $"/api/integration/tickets/{Guid.NewGuid()}", "/api/integration/time-entries?from=2031-01-01" })
        {
            Assert.Equal(401, (await _world.Get(null, url)).Code);
            Assert.Equal(401, (await _world.Get(null, url, new Dictionary<string, string> { ["X-Api-Key"] = "tix_not-a-key" })).Code);
            Assert.Equal(401, (await _world.Get(_world.Admin, url)).Code);
            Assert.Equal(401, (await _world.Get(null, url, new Dictionary<string, string> { ["Authorization"] = "Bearer " + ApiWorld.IntegrationKey })).Code);
        }
    }

    // ======================= time worked, when it is asked for =======================

    [SqlFact]
    public async Task Work_is_left_out_unless_asked_for_and_then_every_session_is_there_with_the_total()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            var id = await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ana, Start, true), (ben, Start, false));
            var anaStart = DateTime.UtcNow.AddHours(-3);
            await _world.Db(async db =>
            {
                db.TicketWorkSessions.Add(new TicketWorkSession { TicketId = id, UserId = ana.Id, StartedAtUtc = anaStart, EndedAtUtc = anaStart.AddMinutes(90) });
                db.TicketWorkSessions.Add(new TicketWorkSession { TicketId = id, UserId = ben.Id, StartedAtUtc = DateTime.UtcNow.AddMinutes(-10), EndedAtUtc = null }); // still working
                return await db.SaveChangesAsync();
            });
            var email = Uri.EscapeDataString(ana.Email);

            // Not asked for: not there, so the answer that was agreed has not grown.
            Assert.False((await One(id)).Json.TryGetProperty("workSessions", out _));
            Assert.False((await One(id)).Json.TryGetProperty("workedSeconds", out _));
            Assert.False(Items(await Picture($"?assigneeEmail={email}"))[0].TryGetProperty("workSessions", out _));

            var asked = (await One(id, "?includeWork=true")).Json;
            var sessions = asked.GetProperty("workSessions").EnumerateArray().ToList();
            Assert.Equal(2, sessions.Count);
            Assert.Equal([ana.Email, ben.Email], sessions.Select(s => s.GetProperty("person").GetProperty("email").GetString()));
            Assert.Equal(anaStart.AddMinutes(90), sessions[0].GetProperty("endedAtUtc").GetDateTime().ToUniversalTime());
            Assert.Equal(JsonValueKind.Null, sessions[1].GetProperty("endedAtUtc").ValueKind); // he has not stopped
            Assert.Equal(new[] { "endedAtUtc", "id", "person", "startedAtUtc" }, Names(sessions[0]));
            Assert.Equal(2, sessions.Select(s => s.GetProperty("id").GetGuid()).Distinct().Count()); // each session has an id of its own, the same in every answer
            Assert.Equal(new[] { "email", "name" }, Names(sessions[0].GetProperty("person")));

            // 90 minutes finished, and a session still running counts up to now: about ten minutes more.
            Assert.InRange(asked.GetProperty("workedSeconds").GetInt64(), 5400 + 600, 5400 + 720);

            var inList = Items(await Picture($"?assigneeEmail={email}&includeWork=true"))[0];
            Assert.Equal(2, inList.GetProperty("workSessions").GetArrayLength());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_ticket_nobody_worked_on_has_an_empty_list_and_zero_seconds_when_work_is_asked_for()
    {
        var ana = await Staff("ana");
        try
        {
            var id = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));

            var asked = (await One(id, "?includeWork=true")).Json;

            Assert.Equal(0, asked.GetProperty("workSessions").GetArrayLength());
            Assert.Equal(0, asked.GetProperty("workedSeconds").GetInt64());
        }
        finally { await CleanUp(); }
    }

    // ======================= tickets of a period =======================

    [SqlFact]
    public async Task Tickets_can_be_asked_for_by_when_they_were_created_the_first_time_included_the_last_not()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            await Ticket("INT-2", TicketStatus.Open, Start.AddDays(1), null, (ana, Start, true));
            await Ticket("INT-3", TicketStatus.Open, Start.AddDays(2), null, (ana, Start, true));
            var email = Uri.EscapeDataString(ana.Email);
            string[] Numbers(ApiResponse r) => Items(r).Select(t => t.GetProperty("ticketNumber").GetString()!).ToArray();

            Assert.Equal(new[] { "INT-3", "INT-2" }, Numbers(await Picture($"?assigneeEmail={email}&createdFrom={At(Start.AddDays(1))}")));
            Assert.Equal(new[] { "INT-2", "INT-1" }, Numbers(await Picture($"?assigneeEmail={email}&createdTo={At(Start.AddDays(2))}")));
            Assert.Equal(new[] { "INT-2" }, Numbers(await Picture($"?assigneeEmail={email}&createdFrom={At(Start.AddDays(1))}&createdTo={At(Start.AddDays(2))}")));
            Assert.Equal(new[] { "INT-2" }, Numbers(await Picture($"?assigneeEmail={email}&createdFrom=2031-01-02&createdTo=2031-01-03"))); // a day is enough
            Assert.Equal(2, (await Picture($"?assigneeEmail={email}&createdFrom=2031-01-02")).Json.GetProperty("totalCount").GetInt32()); // the count follows the period
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_period_of_tickets_that_is_not_a_time_or_ends_before_it_begins_is_refused()
    {
        var nonsense = await Picture("?createdFrom=yesterday");
        var nonsenseTo = await Picture("?createdTo=tomorrow");
        var backwards = await Picture("?createdFrom=2031-02-01&createdTo=2031-01-01");
        var empty = await Picture("?createdFrom=2031-02-01&createdTo=2031-02-01");

        Assert.Equal(400, nonsense.Code);
        Assert.Contains("createdFrom", nonsense.Text);
        Assert.Equal(400, nonsenseTo.Code);
        Assert.Contains("createdTo", nonsenseTo.Text);
        Assert.Equal(400, backwards.Code);
        Assert.Equal(400, empty.Code);
    }

    // ======================= who worked when: the time entries =======================

    [SqlFact]
    public async Task Time_entries_are_the_stretches_of_work_of_a_period_each_on_the_day_it_started_in_the_time_of_the_business()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            var ticket = await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ana, Start, true), (ben, Start.AddMinutes(1), false));
            // 23:30 UTC on the 31st of December is half past midnight on the 1st of January in Sarajevo: the work counts for the 1st.
            var late = await Session(ticket, ana, new DateTime(2030, 12, 31, 23, 30, 0, DateTimeKind.Utc), new DateTime(2031, 1, 1, 1, 0, 0, DateTimeKind.Utc));
            var morning = await Session(ticket, ana, Start.AddHours(9), Start.AddHours(9).AddSeconds(5376)); // 89.6 minutes
            var running = await Session(ticket, ben, Start.AddDays(1).AddHours(8), null);
            await Session(ticket, ana, Start.AddDays(45), Start.AddDays(45).AddHours(1)); // another month
            var email = Uri.EscapeDataString(ana.Email);

            var entries = Items(await Time("?from=2031-01-01&to=2031-01-02"));

            Assert.Equal([late, morning, running], entries.Select(e => e.GetProperty("id").GetGuid()));
            Assert.Equal(["2031-01-01", "2031-01-01", "2031-01-02"], entries.Select(e => e.GetProperty("workDate").GetString()));
            Assert.All(entries, e => { Assert.Equal("Ticket", e.GetProperty("kind").GetString()); Assert.Equal(ticket, e.GetProperty("ticketId").GetGuid()); Assert.Equal("INT-1", e.GetProperty("ticketNumber").GetString()); });
            Assert.Equal([ana.Email, ana.Email, ben.Email], entries.Select(e => e.GetProperty("person").GetProperty("email").GetString()));

            Assert.Equal(5400, entries[0].GetProperty("seconds").GetInt64());
            Assert.Equal(90, entries[0].GetProperty("minutes").GetInt32());
            Assert.Equal(5376, entries[1].GetProperty("seconds").GetInt64());
            Assert.Equal(90, entries[1].GetProperty("minutes").GetInt32()); // rounded to the nearest minute
            Assert.False(entries[1].GetProperty("isRunning").GetBoolean());

            Assert.True(entries[2].GetProperty("isRunning").GetBoolean()); // nobody knows yet how long
            Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("minutes").ValueKind);
            Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("seconds").ValueKind);
            Assert.Equal(JsonValueKind.Null, entries[2].GetProperty("endedAtUtc").ValueKind);

            // The period is in the days of the business: the 31st of December ends at midnight in Sarajevo, an hour before it does in UTC.
            Assert.Empty(Items(await Time($"?from=2030-12-31&to=2030-12-31&email={email}")));
            Assert.Equal([late, morning], Items(await Time($"?from=2031-01-01&to=2031-01-01&email={email}")).Select(e => e.GetProperty("id").GetGuid()));
            Assert.Equal([running], Items(await Time($"?from=2031-01-01&to=2031-01-03&email={Uri.EscapeDataString(ben.Email.ToUpperInvariant())}")).Select(e => e.GetProperty("id").GetGuid()));
            // Without an end it is up to today, and today is before 2031, so the period is just the first day.
            Assert.Equal([late, morning], Items(await Time("?from=2031-01-01")).Select(e => e.GetProperty("id").GetGuid()));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Work_on_a_task_with_no_ticket_is_in_the_same_list_as_such()
    {
        var ana = await Staff("ana");
        try
        {
            var (task, session) = await TaskSession(ana, "Check the contract data", Start.AddDays(2).AddHours(8), Start.AddDays(2).AddHours(8).AddMinutes(30));

            var entry = Items(await Time($"?from=2031-01-03&to=2031-01-03&email={Uri.EscapeDataString(ana.Email)}")).Single();

            Assert.Equal(session, entry.GetProperty("id").GetGuid());
            Assert.Equal("Task", entry.GetProperty("kind").GetString());
            Assert.Equal(task, entry.GetProperty("taskId").GetGuid());
            Assert.Equal("Check the contract data", entry.GetProperty("taskTitle").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("ticketId").ValueKind);
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("ticketNumber").ValueKind);
            Assert.Equal(30, entry.GetProperty("minutes").GetInt32());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Tickets_and_tasks_are_one_list_in_the_order_of_when_the_work_started_and_it_is_paged()
    {
        var ana = await Staff("ana");
        try
        {
            var ticket = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            await Session(ticket, ana, Start.AddHours(8), Start.AddHours(9));
            await TaskSession(ana, "Task A", Start.AddHours(9), Start.AddHours(10));
            await Session(ticket, ana, Start.AddHours(10), Start.AddHours(11));
            await TaskSession(ana, "Task B", Start.AddHours(11), Start.AddHours(12));
            await Session(ticket, ana, Start.AddHours(12), Start.AddHours(13));
            var query = $"?from=2031-01-01&to=2031-01-01&email={Uri.EscapeDataString(ana.Email)}";

            var all = Items(await Time(query + "&pageSize=100"));
            var second = (await Time(query + "&page=2&pageSize=2")).Json;

            Assert.Equal(["Ticket", "Task", "Ticket", "Task", "Ticket"], all.Select(e => e.GetProperty("kind").GetString()));
            Assert.Equal(5, second.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, second.GetProperty("page").GetInt32());
            Assert.Equal(["Ticket", "Task"], second.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("kind").GetString()));
            Assert.Equal(500, (await Time(query + "&pageSize=100000")).Json.GetProperty("pageSize").GetInt32());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_deleted_entry_is_no_longer_in_the_period_which_is_how_the_other_application_finds_it_out()
    {
        var ana = await Staff("ana");
        try
        {
            var ticket = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            var session = await Session(ticket, ana, Start.AddHours(8), Start.AddHours(9));
            var query = $"?from=2031-01-01&to=2031-01-01&email={Uri.EscapeDataString(ana.Email)}";

            Assert.Equal([session], Items(await Time(query)).Select(e => e.GetProperty("id").GetGuid()));
            await _world.Db(async db => await db.TicketWorkSessions.Where(s => s.Id == session).ExecuteDeleteAsync());

            Assert.Empty(Items(await Time(query)));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_period_that_is_not_days_or_is_too_long_is_refused_and_an_address_nobody_has_is_an_empty_answer()
    {
        var missing = await Time("");
        var nonsense = await Time("?from=yesterday");
        var timeNotDay = await Time("?from=2031-01-01T08:00:00Z");
        var nonsenseTo = await Time("?from=2031-01-01&to=tomorrow");
        var backwards = await Time("?from=2031-01-02&to=2031-01-01");
        var longest = await Time("?from=2031-01-01&to=2031-04-02"); // 92 days, the most
        var tooLong = await Time("?from=2031-01-01&to=2031-04-03");
        var nobody = await Time("?from=2031-01-01&to=2031-01-02&email=nobody.has.this@authz.test");

        Assert.Equal(400, missing.Code);
        Assert.Contains("from", missing.Text);
        Assert.Equal(400, nonsense.Code);
        Assert.Equal(400, timeNotDay.Code);
        Assert.Equal(400, nonsenseTo.Code);
        Assert.Equal(400, backwards.Code);
        Assert.Equal(200, longest.Code);
        Assert.Equal(400, tooLong.Code);
        Assert.Contains("92", tooLong.Text);
        Assert.Equal(200, nobody.Code);
        Assert.Empty(Items(nobody));
        Assert.Equal(0, nobody.Json.GetProperty("totalCount").GetInt32());
    }

    [SqlFact]
    public async Task The_time_entries_have_exactly_the_documented_fields_and_nothing_else()
    {
        var ana = await Staff("ana");
        try
        {
            var ticket = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            await Session(ticket, ana, Start.AddHours(8), Start.AddHours(9));

            var response = await Time($"?from=2031-01-01&to=2031-01-01&email={Uri.EscapeDataString(ana.Email)}");

            Assert.Equal(new[] { "items", "page", "pageSize", "totalCount" }, Names(response.Json));
            var entry = Items(response).Single();
            Assert.Equal(
                new[] { "endedAtUtc", "id", "isRunning", "kind", "minutes", "person", "seconds", "startedAtUtc", "taskId", "taskTitle", "ticketId", "ticketNumber", "workDate" },
                Names(entry));
            Assert.Equal(new[] { "email", "name" }, Names(entry.GetProperty("person")));
            Assert.DoesNotContain("passwordHash", response.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ApiWorld.PhoneX, response.Text);
        }
        finally { await CleanUp(); }
    }

    // ======================= what happened to tickets =======================

    [SqlFact]
    public async Task Events_need_a_since_that_is_a_time()
    {
        var missing = await Events("");
        var nonsense = await Events("?since=yesterday");

        Assert.Equal(400, missing.Code);
        Assert.Equal(400, nonsense.Code);
        Assert.Contains("since", missing.Text);
    }

    [SqlFact]
    public async Task The_feed_of_events_says_what_happened_oldest_first_to_whom_and_the_ticket_as_it_is_now()
    {
        var ana = await Staff("ana");
        var ben = await Staff("ben");
        try
        {
            // Ben was on the ticket and has been taken off; Ana is on it now. The events are the history, the ticket in each is the present.
            var id = await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ana, Start, true));
            await Trail(id, "TicketOpened", TicketAuditDetails.Opened(ana.Id, _world.BranchA), Start.AddMinutes(1));
            await Trail(id, "TicketAssigneeAdded", TicketAuditDetails.Added(ben.Id), Start.AddMinutes(2));
            await Trail(id, "TicketAssigneeRemoved", TicketAuditDetails.Removed(ben.Id), Start.AddMinutes(3));
            await Trail(id, "TicketStatusChanged", TicketAuditDetails.StatusChanged(TicketStatus.Open, TicketStatus.InProgress), Start.AddMinutes(4));
            await Trail(id, "TicketTransferred", "Branch a -> b", Start.AddMinutes(5));
            await Trail(id, "TicketClosed", "Ticket #INT-1", Start.AddMinutes(6));
            await Trail(id, "TicketReopened", "Ticket #INT-1", Start.AddMinutes(7));
            // Rows that are not events: another action on a ticket, and the same ticket's id under another kind of thing.
            await Trail(id, "TicketSomethingElse", "x", Start.AddMinutes(8));
            await Trail(id, "TicketClosed", "x", Start.AddMinutes(9), entityType: "Department");

            var response = await Events("?since=" + StartText);

            Assert.Equal(200, response.Code);
            var items = Items(response);
            Assert.Equal(["Opened", "AssigneeAdded", "AssigneeRemoved", "StatusChanged", "Transferred", "Closed", "Reopened"], items.Select(i => i.GetProperty("type").GetString()));
            Assert.Equal([ana.Email, ben.Email, ben.Email, null, null, null, null], items.Select(i => i.GetProperty("person").ValueKind == JsonValueKind.Null ? null : i.GetProperty("person").GetProperty("email").GetString()));
            Assert.Equal(Start.AddMinutes(1), items[0].GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime());
            Assert.All(items, i => Assert.Equal(id, i.GetProperty("ticketId").GetGuid()));

            var change = items[3];
            Assert.Equal("Open", change.GetProperty("fromStatus").GetString());
            Assert.Equal("InProgress", change.GetProperty("toStatus").GetString());
            Assert.Equal(JsonValueKind.Null, items[0].GetProperty("fromStatus").ValueKind);

            // The ticket is as it is NOW: only Ana is on it, though Ben is who the second and third events are about.
            var ticket = items[2].GetProperty("ticket");
            Assert.Equal("INT-1", ticket.GetProperty("ticketNumber").GetString());
            Assert.Equal([ana.Email], ticket.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("email").GetString()));
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task An_event_whose_text_is_not_understood_is_still_reported_only_without_the_person_and_the_statuses()
    {
        var ana = await Staff("ana");
        try
        {
            var id = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            await Trail(id, "TicketAssigneeAdded", "Added somebody", Start.AddMinutes(1));
            await Trail(id, "TicketStatusChanged", "gibberish", Start.AddMinutes(2));
            await Trail(id, "TicketOpened", null, Start.AddMinutes(3));
            await Trail(id, "TicketStatusChanged", "Open -> 7", Start.AddMinutes(4)); // a number is not a status

            var items = Items(await Events("?since=" + StartText));

            Assert.Equal(["AssigneeAdded", "StatusChanged", "Opened", "StatusChanged"], items.Select(i => i.GetProperty("type").GetString()));
            Assert.All(items, i =>
            {
                Assert.Equal(JsonValueKind.Null, i.GetProperty("person").ValueKind);
                Assert.Equal(JsonValueKind.Null, i.GetProperty("fromStatus").ValueKind);
                Assert.Equal(JsonValueKind.Null, i.GetProperty("toStatus").ValueKind);
            });
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task An_event_of_a_ticket_that_is_no_longer_there_is_reported_with_its_id_and_no_ticket()
    {
        var gone = Guid.NewGuid();
        try
        {
            await Trail(gone, "TicketClosed", "Ticket #GONE", Start.AddMinutes(1));

            var item = Items(await Events("?since=" + StartText)).Single();

            Assert.Equal(gone, item.GetProperty("ticketId").GetGuid());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("ticket").ValueKind);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Paging_through_events_of_the_same_instant_loses_none_and_repeats_none()
    {
        var ana = await Staff("ana");
        try
        {
            var id = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            for (var n = 0; n < 5; n++) await Trail(id, "TicketStatusChanged", TicketAuditDetails.StatusChanged(TicketStatus.Open, TicketStatus.InProgress), Start.AddMinutes(5));
            await Trail(id, "TicketClosed", "x", Start.AddMinutes(6));

            var seen = new List<string>();
            var since = StartText;
            string? after = null;
            for (var page = 0; page < 10; page++)
            {
                var response = await Events($"?since={Uri.EscapeDataString(since)}&limit=2" + (after is null ? "" : "&afterId=" + after));
                Assert.Equal(200, response.Code);
                seen.AddRange(Items(response).Select(i => i.GetProperty("eventId").GetString()!));
                var json = response.Json;
                since = json.GetProperty("nextSince").GetDateTime().ToUniversalTime().ToString("o");
                after = json.GetProperty("nextAfterId").ValueKind == JsonValueKind.Null ? null : json.GetProperty("nextAfterId").GetString();
                if (!json.GetProperty("hasMore").GetBoolean()) break;
            }

            Assert.Equal(6, seen.Count);
            Assert.Equal(6, seen.Distinct().Count());

            // Asking again with nothing new gives the same place, so it is safe to ask again and again.
            var nothing = (await Events($"?since={Uri.EscapeDataString(since)}&afterId={after}&limit=2")).Json;
            Assert.Equal(0, nothing.GetProperty("items").GetArrayLength());
            Assert.False(nothing.GetProperty("hasMore").GetBoolean());
            Assert.Equal(after, nothing.GetProperty("nextAfterId").GetString());
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task What_the_application_itself_records_is_what_the_feed_reports()
    {
        // The feed reads the words the services write in the trail. This drives the services, not the database, so that if the words ever
        // drift apart the person or the statuses go missing here and not unnoticed in production.
        var before = DateTime.UtcNow.AddMinutes(-2);
        var id = await Ticket("INT-1", TicketStatus.InProgress, Start, null, (_world.EmpA1, Start, true));
        try
        {
            Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/status", new { status = "Resolved" })).Code);
            Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/assignees", new { userId = _world.EmpA2.Id })).Code);
            Assert.Equal(200, (await _world.Send(_world.EmpA1, HttpMethod.Delete, $"/api/tickets/{id}/assignees/{_world.EmpA2.Id}")).Code);
            Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/close", new { resolutionSummary = "done" })).Code);
            Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/reopen")).Code);

            var mine = Items(await Events($"?since={At(before)}&limit=500")).Where(i => i.GetProperty("ticketId").GetGuid() == id).ToList();

            Assert.Equal(["StatusChanged", "AssigneeAdded", "AssigneeRemoved", "Closed", "Reopened"], mine.Select(i => i.GetProperty("type").GetString()));
            Assert.Equal("InProgress", mine[0].GetProperty("fromStatus").GetString());
            Assert.Equal("Resolved", mine[0].GetProperty("toStatus").GetString());
            Assert.Equal(_world.EmpA2.Email, mine[1].GetProperty("person").GetProperty("email").GetString());
            Assert.Equal(_world.EmpA2.Email, mine[2].GetProperty("person").GetProperty("email").GetString()); // taken off, and still named
            Assert.Equal("Open", mine[4].GetProperty("ticket").GetProperty("status").GetString()); // the ticket is as it is now
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task A_person_taken_off_tickets_because_the_account_was_deactivated_is_reported_as_taken_off_like_any_other()
    {
        var ana = await Staff("ana");
        var before = DateTime.UtcNow.AddMinutes(-2);
        var first = await Ticket("INT-1", TicketStatus.InProgress, Start, null, (ana, Start, true));
        var second = await Ticket("INT-2", TicketStatus.Open, Start, null, (ana, Start, true));
        try
        {
            var deactivated = await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{ana.Id}/active", new { isActive = false });
            Assert.Equal(200, deactivated.Code);

            var removed = Items(await Events($"?since={At(before)}&limit=500"))
                .Where(i => i.GetProperty("type").GetString() == "AssigneeRemoved" && new[] { first, second }.Contains(i.GetProperty("ticketId").GetGuid())).ToList();

            Assert.Equal(2, removed.Count);
            Assert.All(removed, i => Assert.Equal(ana.Email, i.GetProperty("person").GetProperty("email").GetString()));
            Assert.All(removed, i => Assert.Empty(i.GetProperty("ticket").GetProperty("assignees").EnumerateArray())); // and nobody is on the ticket now
        }
        finally { await CleanUp(); }
    }

    // ======================= what is, and what is not, in the answer =======================

    [SqlFact]
    public async Task The_answer_has_exactly_the_documented_fields_and_nothing_else()
    {
        // The fields are the contract with the other application. A field that is added by accident (a message, an internal note, a file) would be
        // given to another application without anybody having decided it should be, so the list is written out here and the test fails on any change.
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            await _world.Db(async db =>
            {
                var id = _made[0];
                db.TicketMessages.Add(new TicketMessage { TicketId = id, AuthorId = ana.Id, Type = MessageType.InternalNote, BodyHtml = "<p>secret internal note</p>" });
                return await db.SaveChangesAsync();
            });

            var feed = await Feed($"?since={StartText}");
            var picture = await Picture($"?assigneeEmail={Uri.EscapeDataString(ana.Email)}");

            Assert.Equal(new[] { "hasMore", "items", "nextAfterId", "nextSince" }, Names(feed.Json));
            var item = Items(feed).Single();
            Assert.Equal(new[] { "assignedAtUtc", "assignee", "assignmentId", "isPrimary", "ticket" }, Names(item));
            Assert.Equal(new[] { "email", "name" }, Names(item.GetProperty("assignee")));
            Assert.Equal(
                new[] { "assignees", "branch", "client", "closedAtUtc", "company", "createdAt", "description", "dueDateUtc", "helpTopic", "id", "openedAtUtc", "priority", "resolutionSummary", "source", "status", "subBranch", "ticketNumber", "title", "url" },
                Names(item.GetProperty("ticket")));
            Assert.Equal(new[] { "assignedAtUtc", "email", "isPrimary", "name" }, Names(item.GetProperty("ticket").GetProperty("assignees")[0]));
            Assert.Equal(new[] { "email", "name" }, Names(item.GetProperty("ticket").GetProperty("client")));
            Assert.Equal(new[] { "items", "page", "pageSize", "totalCount" }, Names(picture.Json));

            Assert.DoesNotContain("secret internal note", feed.Text);
            Assert.DoesNotContain("secret internal note", picture.Text);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task Nothing_in_the_answer_is_a_password_a_token_or_a_phone_number()
    {
        var ana = await Staff("ana");
        try
        {
            await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));

            var text = (await Feed($"?since={StartText}")).Text + (await Picture($"?assigneeEmail={Uri.EscapeDataString(ana.Email)}")).Text;

            Assert.DoesNotContain(ApiWorld.PhoneX, text);
            Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { await CleanUp(); }
    }

    [SqlFact]
    public async Task The_events_and_the_single_ticket_have_exactly_the_documented_fields_and_nothing_else()
    {
        var ana = await Staff("ana");
        try
        {
            var id = await Ticket("INT-1", TicketStatus.Open, Start, null, (ana, Start, true));
            await Trail(id, "TicketAssigneeAdded", TicketAuditDetails.Added(ana.Id), Start.AddMinutes(1));

            var events = await Events("?since=" + StartText);
            var one = await One(id);

            Assert.Equal(new[] { "hasMore", "items", "nextAfterId", "nextSince" }, Names(events.Json));
            var item = Items(events).Single();
            Assert.Equal(new[] { "eventId", "fromStatus", "occurredAtUtc", "person", "ticket", "ticketId", "toStatus", "type" }, Names(item));
            Assert.Equal(new[] { "email", "name" }, Names(item.GetProperty("person")));

            // The ticket inside an event, and the one read alone, are the same ticket as everywhere else.
            var fields = new[] { "assignees", "branch", "client", "closedAtUtc", "company", "createdAt", "description", "dueDateUtc", "helpTopic", "id", "openedAtUtc", "priority", "resolutionSummary", "source", "status", "subBranch", "ticketNumber", "title", "url" };
            Assert.Equal(fields, Names(item.GetProperty("ticket")));
            Assert.Equal(fields, Names(one.Json));

            Assert.DoesNotContain("passwordHash", events.Text + one.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ApiWorld.PhoneX, events.Text + one.Text);
        }
        finally { await CleanUp(); }
    }

    // ======================= the limit =======================

    [SqlFact]
    public async Task Each_key_has_its_own_limit_and_a_loop_that_has_gone_wrong_is_stopped_with_429()
    {
        var codes = new List<int>();
        for (var i = 0; i < 125; i++) codes.Add((await Feed("?since=2099-01-01T00:00:00Z", ApiWorld.IntegrationKeyLimited)).Code);

        Assert.Equal(120, codes.Count(c => c == 200));
        Assert.Equal(5, codes.Count(c => c == 429));
        Assert.Equal(200, (await Feed("?since=2099-01-01T00:00:00Z")).Code); // the other key is not affected
    }
}
