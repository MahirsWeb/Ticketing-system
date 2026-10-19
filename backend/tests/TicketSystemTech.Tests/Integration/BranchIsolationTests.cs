using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Branches are isolated: an employee works with their own branch's tickets, the clients of those tickets and the AI answers
/// built from them, and with nothing else. These tests assert that through the real HTTP API for every person in the seeded
/// world (see <see cref="ApiWorld"/>), including the cases that were once open: the unrouted ticket, the client who writes to
/// two branches, and the client who has not written to any.
/// </summary>
[Collection(ApiCollection.Name)]
public class BranchIsolationTests
{
    private readonly ApiWorld _world;

    public BranchIsolationTests(ApiWorld world) => _world = world;

    private Actor Who(string key) => _world.Actors[key];

    private static string[] Set(string keys) =>
        keys.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => "AZ-" + k).OrderBy(k => k).ToArray();

    // ======================= tickets =======================

    // who sees which tickets (A1, A2 in branch A; B1, B2 in branch B; U not routed to any branch)
    public static TheoryData<string, string> TicketVisibility => new()
    {
        { "admin", "A1,A2,B1,B2,U" },
        { "empA1", "A1,A2" },
        { "empA2", "A1,A2" },
        { "empB1", "B1,B2" },
        { "cliX", "A1,U" },
        { "cliY", "B1" },
        { "cliBoth", "A2,B2" },
        { "cliZ", "" },
    };

    [SqlTheory]
    [MemberData(nameof(TicketVisibility))]
    public async Task The_ticket_list_shows_each_person_only_what_they_may_see(string who, string expected)
    {
        var response = await _world.Get(Who(who), "/api/tickets?pageSize=100");

        Assert.Equal(200, response.Code);
        Assert.Equal(Set(expected), response.Values("ticketNumber", "items").OrderBy(n => n).ToArray());
        Assert.Equal(Set(expected).Length, response.Json.GetProperty("totalCount").GetInt32());
    }

    [SqlTheory]
    [MemberData(nameof(TicketVisibility))]
    public async Task The_ticket_counters_do_not_count_what_the_person_may_not_see(string who, string expected)
    {
        var response = await _world.Get(Who(who), "/api/tickets/counts");

        Assert.Equal(200, response.Code);
        Assert.Equal(Set(expected).Length, response.Json.GetProperty("total").GetInt32());
    }

    [SqlTheory]
    [MemberData(nameof(TicketVisibility))]
    public async Task A_single_ticket_opens_only_for_those_who_may_see_it(string who, string visible)
    {
        var allowed = Set(visible).ToHashSet();

        foreach (var key in _world.Tickets.Keys)
        {
            var response = await _world.Get(Who(who), $"/api/tickets/{_world.Tickets[key]}");
            var expected = allowed.Contains("AZ-" + key) ? 200 : 403;
            Assert.True(response.Code == expected, $"{who} opening ticket {key}: expected {expected}, got {response.Code}");
        }
    }

    [SqlTheory]
    [MemberData(nameof(TicketVisibility))]
    public async Task Filtering_the_list_cannot_widen_what_the_person_sees(string who, string expected)
    {
        // The filters are the obvious place for a hole: ask for the other branch / a client / a company explicitly.
        var visible = Set(expected);
        foreach (var query in new[]
                 {
                     $"departmentId={_world.BranchA}", $"departmentId={_world.BranchB}",
                     $"clientId={_world.CliX.Id}", $"clientId={_world.CliBoth.Id}", $"companyId={_world.CompanyId}",
                     "search=AZ-", "status=New,Open,InProgress,Resolved,Closed"
                 })
        {
            var response = await _world.Get(Who(who), $"/api/tickets?pageSize=100&{query}");
            Assert.Equal(200, response.Code);
            var numbers = response.Values("ticketNumber", "items");
            Assert.True(numbers.All(n => visible.Contains(n)), $"{who} with '{query}' saw {string.Join(",", numbers.Except(visible))}");
        }
    }

    // (actor, ticket) pairs that must be refused for every way of changing or adding to a ticket
    public static TheoryData<string, string> ForeignTickets => new()
    {
        { "empA1", "B1" }, { "empA1", "B2" }, { "empA1", "U" },
        { "empB1", "A1" }, { "empB1", "A2" }, { "empB1", "U" },
        { "cliY", "A1" }, { "cliBoth", "A1" }, { "cliZ", "A2" }, { "cliX", "B1" },
    };

    [SqlTheory]
    [MemberData(nameof(ForeignTickets))]
    public async Task Nobody_can_change_or_add_to_a_ticket_they_may_not_see(string who, string ticket)
    {
        var id = _world.Tickets[ticket];
        var anyId = Guid.NewGuid();
        var actor = Who(who);
        var before = await Snapshot(id);

        var attempts = new List<(string Name, Task<ApiResponse> Call)>
        {
            ("post a message", _world.Post(actor, $"/api/tickets/{id}/messages", new { type = "Response", bodyHtml = "<p>hello</p>" })),
            ("change status", _world.Post(actor, $"/api/tickets/{id}/status", new { status = "Resolved" })),
            ("close", _world.Post(actor, $"/api/tickets/{id}/close", new { resolutionSummary = "done" })),
            ("open", _world.Post(actor, $"/api/tickets/{id}/open", new { source = "Phone", helpTopicId = anyId, departmentId = _world.BranchA, slaPlanId = anyId, priority = "Medium", assignedToUserId = anyId })),
            ("reopen", _world.Post(actor, $"/api/tickets/{id}/reopen")),
            ("transfer to another branch", _world.Post(actor, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchA })),
            ("add an assignee", _world.Post(actor, $"/api/tickets/{id}/assignees", new { userId = _world.EmpA1.Id })),
            ("remove an assignee", _world.Send(actor, HttpMethod.Delete, $"/api/tickets/{id}/assignees/{_world.EmpA1.Id}")),
            ("log a work session", _world.Post(actor, $"/api/tickets/{id}/work-sessions", new { startedAtUtc = DateTime.UtcNow.AddHours(-2), endedAtUtc = DateTime.UtcNow.AddHours(-1) })),
            ("change a work session", _world.Send(actor, HttpMethod.Put, $"/api/tickets/{id}/work-sessions/{anyId}", new { startedAtUtc = DateTime.UtcNow.AddHours(-2) })),
            ("delete a work session", _world.Send(actor, HttpMethod.Delete, $"/api/tickets/{id}/work-sessions/{anyId}")),
            ("upload an attachment", _world.Post(actor, $"/api/tickets/{id}/attachments", UploadForm())),
        };

        foreach (var (name, call) in attempts)
        {
            var response = await call;
            Assert.True(response.Code == 403, $"{who} tried to {name} on ticket {ticket}: expected 403, got {response.Code} {response.Text}");
        }

        // and nothing happened to the ticket
        Assert.Equal(before, await Snapshot(id));
    }

    private Task<(int Messages, int Assignees, int Attachments, TicketStatus Status, Guid? Branch)> Snapshot(Guid ticketId) =>
        _world.Db(async db =>
        {
            var t = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticketId);
            return (await db.TicketMessages.CountAsync(m => m.TicketId == ticketId),
                    await db.TicketAssignees.CountAsync(a => a.TicketId == ticketId),
                    await db.TicketAttachments.CountAsync(a => a.TicketId == ticketId) + await db.TicketWorkSessions.CountAsync(s => s.TicketId == ticketId),
                    t.Status, t.DepartmentId);
        });

    private static MultipartFormDataContent UploadForm()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", "note.pdf");
        return form;
    }

    [SqlFact]
    public async Task A_message_on_a_ticket_of_the_persons_own_branch_does_go_through()
    {
        // The positive control for the test above: the same request shape is accepted where it should be, so the 403s there
        // are about the branch and not about a malformed request.
        var response = await _world.Post(_world.EmpA1, $"/api/tickets/{_world.Tickets["A2"]}/messages", new { type = "Response", bodyHtml = "<p>on it</p>" });

        Assert.Equal(200, response.Code);
    }

    [SqlFact]
    public async Task Staff_cannot_open_a_ticket_into_a_branch_that_is_not_theirs()
    {
        var response = await _world.Post(_world.EmpA1, "/api/tickets/on-behalf", new
        {
            clientEmail = _world.CliZ.Email, title = "Phone call", description = "<p>x</p>", source = "Phone",
            helpTopicId = Guid.NewGuid(), departmentId = _world.BranchB, slaPlanId = Guid.NewGuid(), priority = "Medium",
            assignedToUserId = _world.EmpB1.Id
        });

        Assert.Equal(400, response.Code);
        Assert.Contains("own branch", response.Text);
        Assert.False(await _world.Db(db => db.Tickets.AnyAsync(t => t.ClientId == _world.CliZ.Id)));
    }

    // ======================= clients and staff =======================

    // who sees which CLIENTS (X has a ticket in A; Y in B; Both in each; Z in none)
    public static TheoryData<string, string> ClientVisibility => new()
    {
        { "admin", "cliX,cliY,cliBoth,cliZ" },
        { "empA1", "cliX,cliBoth" },
        { "empA2", "cliX,cliBoth" },
        { "empB1", "cliY,cliBoth" },
    };

    private HashSet<Guid> Ids(string keys) =>
        keys.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => Who(k).Id).ToHashSet();

    private static HashSet<Guid> GuidsOf(ApiResponse response, string? arrayProperty = null) =>
        response.Values("id", arrayProperty).Select(Guid.Parse).ToHashSet();

    [SqlTheory]
    [MemberData(nameof(ClientVisibility))]
    public async Task The_client_list_shows_only_clients_with_a_ticket_in_the_persons_branch(string who, string expected)
    {
        var paged = await _world.Get(Who(who), "/api/users/paged?group=clients&pageSize=100");
        var plain = await _world.Get(Who(who), "/api/users?role=Client");

        Assert.Equal(200, paged.Code);
        Assert.Equal(Ids(expected), GuidsOf(paged, "items"));
        Assert.Equal(Ids(expected).Count, paged.Json.GetProperty("clientCount").GetInt32()); // the tab counter must not leak the total either
        Assert.Equal(200, plain.Code);
        Assert.Equal(Ids(expected), GuidsOf(plain));
    }

    [SqlTheory]
    [MemberData(nameof(ClientVisibility))]
    public async Task Searching_cannot_find_a_client_the_person_may_not_see(string who, string expected)
    {
        foreach (var name in new[] { "cliX", "cliY", "cliBoth", "cliZ" })
        {
            var byName = await _world.Get(Who(who), $"/api/users/paged?group=clients&search={name}");
            var byEmail = await _world.Get(Who(who), $"/api/users/paged?group=clients&search={Uri.EscapeDataString(Who(name).Email)}");
            var shouldFind = Ids(expected).Contains(Who(name).Id);

            Assert.Equal(shouldFind, GuidsOf(byName, "items").Contains(Who(name).Id));
            Assert.Equal(shouldFind, GuidsOf(byEmail, "items").Contains(Who(name).Id));
        }
    }

    [SqlTheory]
    [MemberData(nameof(ClientVisibility))]
    public async Task A_client_profile_opens_only_for_those_who_may_see_that_client(string who, string visible)
    {
        foreach (var name in new[] { "cliX", "cliY", "cliBoth", "cliZ" })
        {
            var response = await _world.Get(Who(who), $"/api/users/{Who(name).Id}");
            var expected = Ids(visible).Contains(Who(name).Id) ? 200 : 404;
            Assert.True(response.Code == expected, $"{who} opening {name}: expected {expected}, got {response.Code}");
        }
    }

    [SqlFact]
    public async Task Staff_see_their_own_branch_only_and_a_client_cannot_list_anyone()
    {
        var a = await _world.Get(_world.EmpA1, "/api/users/paged?group=staff&pageSize=100");
        var b = await _world.Get(_world.EmpB1, "/api/users/paged?group=staff&pageSize=100");
        var admin = await _world.Get(_world.Admin, "/api/users/paged?group=staff&pageSize=100");

        Assert.Equal(new HashSet<Guid> { _world.EmpA1.Id, _world.EmpA2.Id }, GuidsOf(a, "items"));
        Assert.Equal(new HashSet<Guid> { _world.EmpB1.Id }, GuidsOf(b, "items"));
        Assert.True(GuidsOf(admin, "items").IsSupersetOf(new[] { _world.Admin.Id, _world.EmpA1.Id, _world.EmpB1.Id }));

        Assert.Equal(404, (await _world.Get(_world.EmpA1, $"/api/users/{_world.EmpB1.Id}")).Code);
        Assert.Equal(200, (await _world.Get(_world.EmpA1, $"/api/users/{_world.EmpA2.Id}")).Code);
        Assert.Equal(403, (await _world.Get(_world.CliX, "/api/users/paged")).Code);
        Assert.Equal(403, (await _world.Get(_world.CliX, $"/api/users/{_world.CliX.Id}")).Code);
    }

    [SqlFact]
    public async Task Choosing_an_assignee_in_another_branch_shows_that_branchs_staff_but_still_no_clients()
    {
        // Staff may route a ticket to any branch, so asking "who is in branch B?" has to work for an employee of A.
        var response = await _world.Get(_world.EmpA1, $"/api/users?departmentId={_world.BranchB}");

        Assert.Equal(200, response.Code);
        var ids = GuidsOf(response);
        Assert.Contains(_world.EmpB1.Id, ids);
        Assert.DoesNotContain(_world.CliY.Id, ids);
        Assert.DoesNotContain(_world.CliZ.Id, ids);
    }

    [SqlFact]
    public async Task Looking_a_client_up_by_exact_email_works_everywhere_but_hides_the_phone_and_leaves_a_trace_outside_the_branch()
    {
        var inside = await _world.Get(_world.EmpA1, $"/api/users/lookup-by-email?email={Uri.EscapeDataString(_world.CliX.Email)}");
        var outside = await _world.Get(_world.EmpA1, $"/api/users/lookup-by-email?email={Uri.EscapeDataString(_world.CliY.Email)}");
        var admin = await _world.Get(_world.Admin, $"/api/users/lookup-by-email?email={Uri.EscapeDataString(_world.CliY.Email)}");
        var unknown = await _world.Get(_world.EmpA1, "/api/users/lookup-by-email?email=nobody%40authz.test");

        Assert.Equal(200, inside.Code);
        Assert.Equal(ApiWorld.PhoneX, inside.Json.GetProperty("phoneNumber").GetString());

        Assert.Equal(200, outside.Code);
        Assert.Equal(_world.CliY.Id, outside.Json.GetProperty("id").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, outside.Json.GetProperty("phoneNumber").ValueKind); // not shown to a branch the client never wrote to

        Assert.Equal(ApiWorld.PhoneY, admin.Json.GetProperty("phoneNumber").GetString());
        Assert.Equal(404, unknown.Code);

        var traces = await _world.Db(db => db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "ClientLookupOutsideBranch").Select(a => new { a.UserId, a.EntityId }).ToListAsync());
        Assert.Contains(traces, t => t.UserId == _world.EmpA1.Id && t.EntityId == _world.CliY.Id);
        Assert.DoesNotContain(traces, t => t.EntityId == _world.CliX.Id); // looking up someone of your own branch is not an event
    }

    // ======================= AI assistant =======================

    private async Task<(List<string> Tickets, List<string> Documents)> SearchAsync(Actor who, string path)
    {
        var response = await _world.Get(who, path);
        Assert.Equal(200, response.Code);
        var items = response.Json.ValueKind == System.Text.Json.JsonValueKind.Array ? response.Json : response.Json.GetProperty("sources");
        var tickets = items.EnumerateArray().Where(e => e.GetProperty("ticketNumber").ValueKind != System.Text.Json.JsonValueKind.Null)
            .Select(e => e.GetProperty("ticketNumber").GetString()!).OrderBy(n => n).ToList();
        var documents = items.EnumerateArray().Where(e => e.GetProperty("documentId").ValueKind != System.Text.Json.JsonValueKind.Null)
            .Select(e => e.GetProperty("title").GetString()!).ToList();
        return (tickets, documents);
    }

    // The same expectations hold on the semantic path (an embedding is available) and on the keyword path (none is).
    public static TheoryData<string, string, bool> AiSources => new()
    {
        { "admin", "A1,B1", true }, { "empA1", "A1", true }, { "empB1", "B1", true },
        { "admin", "A1,B1", false }, { "empA1", "A1", false }, { "empB1", "B1", false },
    };

    [SqlTheory]
    [MemberData(nameof(AiSources))]
    public async Task AI_search_draws_only_on_tickets_of_the_persons_branch_plus_the_shared_documentation(string who, string expected, bool semantic)
    {
        _world.Factory.Embeddings.Enabled = semantic;
        try
        {
            var (tickets, documents) = await SearchAsync(Who(who), "/api/knowledge-base/search?query=zebra&take=50");

            Assert.Equal(Set(expected), tickets.ToArray());
            Assert.Single(documents); // the documentation is shared by everybody
        }
        finally { _world.Factory.Embeddings.Enabled = true; }
    }

    [SqlTheory]
    [MemberData(nameof(AiSources))]
    public async Task What_the_AI_is_shown_as_context_is_limited_the_same_way(string who, string expected, bool semantic)
    {
        _world.Factory.Embeddings.Enabled = semantic;
        try
        {
            var response = await _world.Post(Who(who), "/api/knowledge-base/ask", new { question = "zebra problem" });
            var context = _world.Factory.Chat.LastContext!;

            Assert.Equal(200, response.Code);
            Assert.Equal(expected.Contains("A1"), context.Contains("marker-ticket-A"));
            Assert.Equal(expected.Contains("B1"), context.Contains("marker-ticket-B"));
            Assert.Contains("marker-documentation", context);
        }
        finally { _world.Factory.Embeddings.Enabled = true; }
    }

    [SqlTheory]
    [InlineData("admin", 2)]
    [InlineData("empA1", 1)]
    [InlineData("empB1", 1)]
    public async Task The_similar_tickets_report_counts_only_the_persons_branch(string who, int eligible)
    {
        var response = await _world.Get(Who(who), "/api/knowledge-base/similar-tickets?query=zebra");

        Assert.Equal(200, response.Code);
        Assert.Equal(eligible, response.Json.GetProperty("totalEligibleTicketCount").GetInt32());
    }

    [SqlTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_client_gets_documentation_only_never_a_ticket(bool semantic)
    {
        _world.Factory.Embeddings.Enabled = semantic;
        try
        {
            var response = await _world.Post(_world.CliBoth, "/api/knowledge-base/ask-client", new { question = "zebra problem" });

            Assert.Equal(200, response.Code);
            var sources = response.Json.GetProperty("sources").EnumerateArray().ToList();
            Assert.NotEmpty(sources);
            Assert.All(sources, s => Assert.Equal(System.Text.Json.JsonValueKind.Null, s.GetProperty("ticketNumber").ValueKind));
            Assert.DoesNotContain("marker-ticket", _world.Factory.Chat.LastContext);
        }
        finally { _world.Factory.Embeddings.Enabled = true; }
    }

    [SqlTheory]
    [InlineData("empA1", "A1")]
    [InlineData("empB1", "B1")]
    public async Task Another_branchs_better_matches_do_not_push_the_persons_own_results_out(string who, string ownTicket)
    {
        // Branch B's ticket and the documentation match the question best; A's ticket is third. With room for two results,
        // an employee of branch A must still get A's ticket: the other branch's ticket is removed before ranking, it does
        // not take a place in the result list that is only emptied afterwards.
        var (tickets, documents) = await SearchAsync(Who(who), "/api/knowledge-base/search?query=zebra&take=2");

        Assert.Equal(new[] { "AZ-" + ownTicket }, tickets);
        Assert.Single(documents);
    }

    [SqlTheory]
    [InlineData("empA1")]
    [InlineData("empB1")]
    public async Task The_similar_tickets_figures_are_computed_from_the_persons_branch_only(string who)
    {
        var response = await _world.Get(Who(who), "/api/knowledge-base/similar-tickets?query=zebra");

        Assert.Equal(200, response.Code);
        Assert.Equal(1, response.Json.GetProperty("similarTicketCount").GetInt32());
        Assert.Equal(1, response.Json.GetProperty("totalEligibleTicketCount").GetInt32());
        Assert.Single(response.Json.GetProperty("tickets").EnumerateArray());
    }

    // ======================= profile pictures =======================

    private static MultipartFormDataContent PictureForm()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "me.png");
        return form;
    }

    [SqlFact]
    public async Task Only_the_person_themselves_or_an_admin_may_change_a_profile_picture()
    {
        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/users/{_world.EmpA2.Id}/profile-picture", PictureForm())).Code);
        Assert.Equal(403, (await _world.Post(_world.EmpB1, $"/api/users/{_world.CliX.Id}/profile-picture", PictureForm())).Code);
        Assert.Equal(403, (await _world.Post(_world.CliX, $"/api/users/{_world.CliY.Id}/profile-picture", PictureForm())).Code);

        // the same request is fine for oneself and for the admin, so the refusals above are about who is asking
        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/users/{_world.EmpA1.Id}/profile-picture", PictureForm())).Code);
        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/users/{_world.CliZ.Id}/profile-picture", PictureForm())).Code);
    }

    // ======================= tasks and notifications =======================

    [SqlFact]
    public async Task Work_tasks_belong_to_a_branch_too()
    {
        var a = await _world.Get(_world.EmpA1, "/api/work-tasks");
        var b = await _world.Get(_world.EmpB1, "/api/work-tasks");
        var admin = await _world.Get(_world.Admin, "/api/work-tasks");

        Assert.Equal(new[] { "Task A" }, a.Values("title"));
        Assert.Equal(new[] { "Task B" }, b.Values("title"));
        Assert.Equal(2, admin.Json.GetArrayLength());
        // asking for the other branch explicitly does not widen it
        Assert.Equal(0, (await _world.Get(_world.EmpA1, $"/api/work-tasks?departmentId={_world.BranchB}")).Json.GetArrayLength());

        Assert.Equal(200, (await _world.Get(_world.EmpA1, $"/api/work-tasks/{_world.WorkTasks["A"]}")).Code);
        Assert.Equal(403, (await _world.Get(_world.EmpA1, $"/api/work-tasks/{_world.WorkTasks["B"]}")).Code);
        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{_world.WorkTasks["B"]}/reassign", new { assignedToUserId = _world.EmpA2.Id })).Code);
        Assert.Equal(403, (await _world.Send(_world.EmpA1, HttpMethod.Patch, $"/api/work-tasks/{_world.WorkTasks["B"]}/time", new { startedAtUtc = DateTime.UtcNow })).Code);
        Assert.Equal(403, (await _world.Post(_world.EmpA1, $"/api/work-tasks/{_world.WorkTasks["B"]}/start")).Code);

        // the team timelines
        Assert.Equal(403, (await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt?userId={_world.EmpB1.Id}")).Code);
        Assert.Equal(403, (await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt/branch?departmentId={_world.BranchB}")).Code);
        Assert.Equal(200, (await _world.Get(_world.EmpA1, $"/api/work-tasks/gantt/branch?departmentId={_world.BranchA}")).Code);
        Assert.Equal(200, (await _world.Get(_world.Admin, $"/api/work-tasks/gantt/branch?departmentId={_world.BranchB}")).Code);

        // clients have no access to tasks at all
        Assert.Equal(403, (await _world.Get(_world.CliX, "/api/work-tasks")).Code);
    }

    [SqlFact]
    public async Task Notifications_are_private_to_their_owner()
    {
        var a = await _world.Get(_world.EmpA1, "/api/notifications");
        var b = await _world.Get(_world.EmpB1, "/api/notifications");

        Assert.Equal(new[] { "for A1" }, a.Values("message"));
        Assert.Equal(new[] { "for B1" }, b.Values("message"));

        // marking someone else's notification as read looks like it does not exist
        Assert.Equal(404, (await _world.Post(_world.EmpA1, $"/api/notifications/{_world.Notifications["B1"]}/read")).Code);
        Assert.False(await _world.Db(db => db.Notifications.Where(n => n.Id == _world.Notifications["B1"]).Select(n => n.IsRead).SingleAsync()));
    }
}
