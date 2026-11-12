using System.Text.Json;
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
        await _world.Db(async db =>
        {
            db.Tickets.RemoveRange(db.Tickets.Where(t => ids.Contains(t.Id))); // the assignees go with their tickets
            return await db.SaveChangesAsync();
        });
    }

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
