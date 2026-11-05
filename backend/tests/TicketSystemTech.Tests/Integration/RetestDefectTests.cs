using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Defects found by the second round of testing (2026-11-03), each written the way the tester proved it: the same requests
/// against the real application, the same query of the database afterwards. Every test cleans up the rows it made.
/// </summary>
[Collection(ApiCollection.Name)]
public class RetestDefectTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _messages = new();
    private readonly List<Guid> _lookups = new();

    public RetestDefectTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.TicketMessages.Where(m => _messages.Contains(m.Id) || _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.IdempotencyRecords.Where(r => r.Key.StartsWith("retest-")).ExecuteDeleteAsync();
            await db.KnowledgeBaseChunks.Where(c => c.TicketId != null && _tickets.Contains(c.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && _tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
            return await db.SlaPlans.Where(s => _lookups.Contains(s.Id)).ExecuteDeleteAsync();
        });
        _world.Factory.Embeddings.Enabled = true;
    }

    private async Task<Guid> NewTicketAsync(TicketStatus status, Guid? branch = null, Action<Ticket>? customise = null)
    {
        var ticket = new Ticket
        {
            TicketNumber = "RT-" + Guid.NewGuid().ToString("N")[..8],
            Title = "retest",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = branch ?? _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem
        };
        customise?.Invoke(ticket);
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private static Dictionary<string, string> Key(string key) => new() { ["Idempotency-Key"] = key };

    // ======================= TS-ARC-02.1: leaving Closed =======================

    [SqlFact]
    public async Task A_ticket_taken_out_of_Closed_through_the_status_endpoint_loses_its_closed_by_and_closed_at_and_can_be_flagged_overdue_again()
    {
        var id = await NewTicketAsync(TicketStatus.Closed, customise: t =>
        {
            t.ClosedAtUtc = DateTime.UtcNow.AddHours(-2);
            t.ClosedByUserId = _world.EmpA1.Id;
            t.OverdueNotifiedAtUtc = DateTime.UtcNow.AddDays(-1);
        });

        var response = await _world.Post(_world.EmpA1, $"/api/tickets/{id}/status", new { status = "Open" });

        Assert.Equal(200, response.Code);
        var ticket = await _world.Db(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Id == id));
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ClosedAtUtc);
        Assert.Null(ticket.ClosedByUserId);
        Assert.Null(ticket.OverdueNotifiedAtUtc);
    }

    [SqlFact]
    public async Task The_reopen_endpoint_clears_the_same_fields()
    {
        var id = await NewTicketAsync(TicketStatus.Closed, customise: t =>
        {
            t.ClosedAtUtc = DateTime.UtcNow.AddHours(-2);
            t.ClosedByUserId = _world.EmpA1.Id;
            t.OverdueNotifiedAtUtc = DateTime.UtcNow.AddDays(-1);
        });

        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/reopen")).Code);

        var ticket = await _world.Db(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Id == id));
        Assert.Equal((null, null, null), (ticket.ClosedAtUtc, ticket.ClosedByUserId, ticket.OverdueNotifiedAtUtc));
    }

    // ======================= TS-35.1: two requests racing for the same thing =======================

    [SqlFact]
    public async Task A_unique_index_that_refuses_a_second_copy_is_a_409_not_a_500()
    {
        var id = await NewTicketAsync(TicketStatus.InProgress);
        DbUpdateException? refused = null;
        try
        {
            await _world.Db(async db =>
            {
                db.TicketAssignees.AddRange(new TicketAssignee { TicketId = id, UserId = _world.EmpA1.Id }, new TicketAssignee { TicketId = id, UserId = _world.EmpA1.Id });
                return await db.SaveChangesAsync();
            });
        }
        catch (DbUpdateException ex)
        {
            refused = ex;
        }
        Assert.NotNull(refused);

        var context = new DefaultHttpContext { TraceIdentifier = "trace-dup" };
        context.Response.Body = new MemoryStream();
        await new ApiExceptionMiddleware(_ => throw refused!, NullLogger<ApiExceptionMiddleware>.Instance, new HostEnvironmentStub())
            .InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = JsonDocument.Parse(context.Response.Body).RootElement;
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("DUPLICATE", body.GetProperty("code").GetString());
    }

    [SqlFact]
    public async Task Two_people_opening_the_same_new_ticket_at_once_get_one_success_and_one_conflict_and_never_a_500()
    {
        var (topic, plan) = await LookupsAsync();
        foreach (var sameAssignee in new[] { false, true }) // TS-35.1 variants A and B
        {
            var id = await NewTicketAsync(TicketStatus.New);
            object Body(Actor assignee) => new
            {
                source = "TicketSystem",
                helpTopicId = topic,
                departmentId = _world.BranchA,
                subBranchId = (Guid?)null,
                slaPlanId = plan,
                priority = "Medium",
                dueDateUtc = (DateTime?)null,
                assignedToUserId = assignee.Id,
                categoryId = (Guid?)null
            };

            var first = _world.Post(_world.EmpA1, $"/api/tickets/{id}/open", Body(_world.EmpA1));
            var second = _world.Post(_world.EmpA2, $"/api/tickets/{id}/open", Body(sameAssignee ? _world.EmpA1 : _world.EmpA2));
            var codes = (await Task.WhenAll(first, second)).Select(r => r.Code).OrderBy(c => c).ToList();

            Assert.DoesNotContain(500, codes);
            Assert.Equal(200, codes[0]);
            Assert.Contains(codes[1], new[] { 400, 409 }); // "only new tickets can be opened" or "changed by someone else"

            var (assigned, events) = await _world.Db(async db => (
                await db.TicketAssignees.CountAsync(a => a.TicketId == id),
                await db.TicketMessages.CountAsync(m => m.TicketId == id && m.Type == MessageType.SystemEvent && m.BodyHtml.Contains("Ticket opened by"))));
            Assert.Equal(1, assigned);
            Assert.Equal(1, events);
        }
    }

    private async Task<(Guid Topic, Guid Plan)> LookupsAsync()
    {
        var topic = new HelpTopic { Name = "Retest " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "Retest " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db => { db.AddRange(topic, plan); return await db.SaveChangesAsync(); });
        _lookups.Add(topic.Id);
        _lookups.Add(plan.Id);
        return (topic.Id, plan.Id);
    }

    // ======================= TS-32.3: a reversed date range =======================

    [SqlTheory]
    [InlineData("summary")]
    [InlineData("timeseries")]
    [InlineData("leaderboard")]
    [InlineData("export")]
    public async Task A_report_for_a_period_that_ends_before_it_starts_is_refused(string report)
    {
        var reversed = await _world.Get(_world.EmpA1, $"/api/reports/{report}?from=2026-12-31&to=2026-01-01");
        var sensible = await _world.Get(_world.EmpA1, $"/api/reports/{report}?from=2026-01-01&to=2026-12-31");

        Assert.Equal(400, reversed.Code);
        Assert.Contains("start date", reversed.Text);
        Assert.NotEqual(400, sensible.Code);
    }

    // ======================= TS-ARC-03.1: the same message sent twice =======================

    private async Task<JsonElement> PostNoteAsync(Actor actor, Guid ticket, string text, string? key)
    {
        var response = await _world.Send(actor, HttpMethod.Post, $"/api/tickets/{ticket}/messages",
            new { type = "InternalNote", bodyHtml = $"<p>{text}</p>" }, key is null ? null : Key(key));
        Assert.Equal(200, response.Code);
        var message = response.Json.Clone();
        _messages.Add(message.GetProperty("id").GetGuid());
        return message;
    }

    [SqlFact]
    public async Task A_message_sent_twice_with_the_same_key_is_one_message_and_the_second_answer_is_the_first()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);

        var first = await PostNoteAsync(_world.EmpA1, ticket, "TST-DUPLIKAT-ARC03", "retest-msg-1");
        var second = await PostNoteAsync(_world.EmpA1, ticket, "TST-DUPLIKAT-ARC03", "retest-msg-1");

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1, await _world.Db(db => db.TicketMessages.CountAsync(m => m.TicketId == ticket && m.BodyHtml.Contains("TST-DUPLIKAT-ARC03"))));
    }

    [SqlFact]
    public async Task Different_keys_or_no_key_are_different_messages()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);

        await PostNoteAsync(_world.EmpA1, ticket, "same text", "retest-msg-a");
        await PostNoteAsync(_world.EmpA1, ticket, "same text", "retest-msg-b");
        await PostNoteAsync(_world.EmpA1, ticket, "same text", null);

        Assert.Equal(3, await _world.Db(db => db.TicketMessages.CountAsync(m => m.TicketId == ticket && m.Type == MessageType.InternalNote)));
    }

    [SqlFact]
    public async Task One_persons_key_cannot_make_another_persons_message_disappear()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);

        var mine = await PostNoteAsync(_world.EmpA1, ticket, "from A1", "retest-msg-shared");
        var theirs = await PostNoteAsync(_world.EmpA2, ticket, "from A2", "retest-msg-shared");

        Assert.NotEqual(mine.GetProperty("id").GetGuid(), theirs.GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task A_malformed_key_is_refused_not_ignored()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);

        var response = await _world.Send(_world.EmpA1, HttpMethod.Post, $"/api/tickets/{ticket}/messages",
            new { type = "InternalNote", bodyHtml = "<p>x</p>" }, Key("not valid because of spaces"));

        Assert.Equal(400, response.Code);
    }

    // ======================= TS-DEP-04.3 / 04.1: vectors of another model =======================

    private async Task<Guid> TicketWithChunkAsync(float[] vector, string? model)
    {
        var id = await NewTicketAsync(TicketStatus.Closed, customise: t => t.ClosedAtUtc = DateTime.UtcNow);
        await _world.Db(async db =>
        {
            db.TicketMessages.Add(new TicketMessage { TicketId = id, AuthorId = _world.EmpA1.Id, Type = MessageType.InternalNote, BodyHtml = new string('n', 200) });
            db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk { TicketId = id, Content = "retest chunk", Embedding = vector, EmbeddingModel = model });
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<(float[]? Vector, string? Model)> ChunkOfAsync(Guid ticket)
    {
        var chunk = await _world.Db(db => db.KnowledgeBaseChunks.AsNoTracking().SingleAsync(c => c.TicketId == ticket));
        return (chunk.Embedding, chunk.EmbeddingModel);
    }

    private async Task AssertChunkAsync(Guid ticket, float[] expectedVector, string expectedModel)
    {
        var (vector, model) = await ChunkOfAsync(ticket);
        Assert.Equal(expectedVector, vector);
        Assert.Equal(expectedModel, model);
    }

    private async Task RunReindexAsync()
    {
        var started = await _world.Post(_world.Admin, "/api/knowledge-base/reindex-all");
        Assert.True(started.Code is 202 or 409, started.Text);
        for (var i = 0; i < 100; i++)
        {
            var status = await _world.Get(_world.Admin, "/api/knowledge-base/reindex-status");
            if (status.Json.GetProperty("state").ToString() is "Completed" or "Failed" or "Idle" && i > 0) return;
            await Task.Delay(100);
        }
        Assert.Fail("the reindex did not finish");
    }

    [SqlFact]
    public async Task The_reindex_rebuilds_a_vector_that_was_made_by_another_model_or_has_another_size()
    {
        var otherSize = await TicketWithChunkAsync(new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f }, model: null);          // 5 numbers, 3 expected
        var otherModel = await TicketWithChunkAsync(new[] { 0.5f, 0.5f, 0.5f }, model: "an-older-model");         // right size, wrong model
        var current = await TicketWithChunkAsync(new[] { 0f, 1f, 0f }, model: FakeEmbeddingService.Model);        // fine as it is

        await RunReindexAsync();

        await AssertChunkAsync(otherSize, new[] { 1f, 0f, 0f }, FakeEmbeddingService.Model);
        await AssertChunkAsync(otherModel, new[] { 1f, 0f, 0f }, FakeEmbeddingService.Model);
        await AssertChunkAsync(current, new[] { 0f, 1f, 0f }, FakeEmbeddingService.Model);
    }

    [SqlFact]
    public async Task Without_an_embedding_provider_the_leftover_vector_is_still_forgotten_instead_of_lingering_for_ever()
    {
        var stale = await TicketWithChunkAsync(new[] { 0.1f, 0.2f, 0.3f }, model: "an-older-model");
        _world.Factory.Embeddings.Enabled = false;

        await RunReindexAsync();

        var (vector, model) = await ChunkOfAsync(stale);
        Assert.Null(vector);
        Assert.Null(model);
    }

    // ======================= TS-OPS-03.1: the keyword path =======================

    [SqlFact]
    public async Task The_keyword_search_returns_the_best_matches_first_and_no_more_than_asked_for()
    {
        var word = "kwtest" + Guid.NewGuid().ToString("N")[..6];
        // three chunks that contain 1, 2 and 3 of the three search words
        var ids = new List<Guid>();
        foreach (var (content, i) in new[] { $"{word}alpha only", $"{word}alpha and {word}beta", $"{word}alpha {word}beta {word}gamma" }.Select((c, i) => (c, i)))
        {
            var ticket = await NewTicketAsync(TicketStatus.Closed, customise: t => { t.ClosedAtUtc = DateTime.UtcNow; t.Title = "kw " + i; });
            ids.Add(ticket);
            await _world.Db(async db =>
            {
                db.TicketMessages.Add(new TicketMessage { TicketId = ticket, AuthorId = _world.EmpA1.Id, Type = MessageType.InternalNote, BodyHtml = new string('n', 200) });
                db.KnowledgeBaseChunks.Add(new KnowledgeBaseChunk { TicketId = ticket, Content = content });
                return await db.SaveChangesAsync();
            });
        }
        _world.Factory.Embeddings.Enabled = false; // no embedding provider: the keyword path

        var query = Uri.EscapeDataString($"{word}alpha {word}beta {word}gamma");
        var all = await _world.Get(_world.EmpA1, $"/api/knowledge-base/search?query={query}&take=10");
        var top2 = await _world.Get(_world.EmpA1, $"/api/knowledge-base/search?query={query}&take=2");

        Assert.Equal(200, all.Code);
        Assert.Equal(3, all.Json.GetArrayLength());
        Assert.Equal(2, top2.Json.GetArrayLength());
        var topScores = top2.Json.EnumerateArray().Select(e => e.GetProperty("matchScore").GetInt32()).OrderByDescending(s => s).ToList();
        Assert.Equal(new[] { 3, 2 }, topScores); // the cut-off kept the best two, not two arbitrary ones
    }

    // ======================= AI availability =======================

    [SqlFact]
    public async Task The_answer_says_whether_an_AI_model_could_have_given_it()
    {
        var withAi = await _world.Post(_world.EmpA1, "/api/knowledge-base/ask", new { question = "zebra" });
        Assert.True(withAi.Json.GetProperty("aiAvailable").GetBoolean());

        var chat = _world.Factory.Services.GetRequiredService<IChatCompletionService>();
        Assert.True(chat.IsAvailable);
    }

    // ======================= TS-ONP-03.1 / 03.2: the host name of a request =======================

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    private async Task ResetPictureAsync(Actor person)
    {
        var storage = _world.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<IFileStorage>();
        var stored = await _world.Db(db => db.Users.Where(u => u.Id == person.Id).Select(u => u.ProfilePictureUrl).SingleAsync());
        await storage.DeletePublicImageAsync(stored);
        await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePictureUrl, (string?)null)));
    }

    [SqlFact]
    public async Task A_request_for_a_host_name_the_application_is_not_configured_for_is_refused()
    {
        var other = await _world.Get(_world.EmpA1, "/api/health", new Dictionary<string, string> { ["Host"] = "evil.example.test" });
        var own = await _world.Get(_world.EmpA1, "/api/health");

        Assert.Equal(400, other.Code);
        Assert.Equal(200, own.Code);
    }

    [SqlFact]
    public async Task A_profile_picture_is_stored_as_a_path_and_the_address_is_put_in_front_when_it_is_read()
    {
        var person = await _world.CreateActorAsync("avatarOwner", UserRole.Employee, _world.BranchA, null);
        try
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(TinyPng), "File", "me.png" } };
            var upload = await _world.Send(person, HttpMethod.Post, $"/api/users/{person.Id}/profile-picture", form);

            Assert.Equal(200, upload.Code);
            var stored = await _world.Db(db => db.Users.Where(u => u.Id == person.Id).Select(u => u.ProfilePictureUrl).SingleAsync());
            Assert.StartsWith("/avatars/", stored);
            Assert.DoesNotContain("://", stored); // the database does not remember the address the uploader used

            var shown = upload.Json.GetProperty("profilePictureUrl").GetString();
            Assert.Equal("http://localhost" + stored, shown);
            var listed = (await _world.Get(_world.Admin, $"/api/users/{person.Id}")).Json.GetProperty("profilePictureUrl").GetString();
            Assert.Equal(shown, listed);
        }
        finally
        {
            await ResetPictureAsync(person);
            await _world.Db(db => db.Users.Where(u => u.Id == person.Id).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Profile_pictures_saved_with_an_absolute_address_are_rewritten_once_and_other_values_are_left_alone()
    {
        var key = Guid.NewGuid() + ".png";
        var one = await _world.CreateActorAsync("avatarOld1", UserRole.Employee, _world.BranchA, null);
        var two = await _world.CreateActorAsync("avatarOld2", UserRole.Employee, _world.BranchA, null);
        try
        {
            await _world.Db(async db =>
            {
                await db.Users.Where(u => u.Id == one.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePictureUrl, $"http://some-old-machine:5114/avatars/{key}"));
                return await db.Users.Where(u => u.Id == two.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePictureUrl, "https://cdn.example/other.png"));
            });

            // Even before the rewrite, what leaves the API is built from the request, not from the old stored address.
            var before = (await _world.Get(_world.Admin, $"/api/users/{one.Id}")).Json.GetProperty("profilePictureUrl").GetString();
            Assert.Equal($"http://localhost/avatars/{key}", before);

            var changed = await _world.Db(db => AvatarPathBackfill.RunAsync(db, NullLogger.Instance));

            Assert.True(changed >= 1);
            var stored = await _world.Db(db => db.Users.Where(u => u.Id == one.Id || u.Id == two.Id).OrderBy(u => u.FirstName).Select(u => u.ProfilePictureUrl).ToListAsync());
            Assert.Equal(new[] { $"/avatars/{key}", "https://cdn.example/other.png" }, stored);
            Assert.Equal(0, await _world.Db(db => AvatarPathBackfill.RunAsync(db, NullLogger.Instance))); // nothing left to do the second time
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == one.Id || u.Id == two.Id).ExecuteDeleteAsync());
        }
    }

    // ======================= TS-15.3: one rule for who may be put on a ticket =======================

    [SqlFact]
    public async Task Every_way_of_putting_someone_on_a_ticket_refuses_an_agent_of_another_branch()
    {
        var (topic, plan) = await LookupsAsync();
        var fresh = await NewTicketAsync(TicketStatus.New);
        var running = await NewTicketAsync(TicketStatus.InProgress);

        var open = await _world.Post(_world.Admin, $"/api/tickets/{fresh}/open", new
        {
            source = "TicketSystem",
            helpTopicId = topic,
            departmentId = _world.BranchA,
            subBranchId = (Guid?)null,
            slaPlanId = plan,
            priority = "Medium",
            dueDateUtc = (DateTime?)null,
            assignedToUserId = _world.EmpB1.Id,
            categoryId = (Guid?)null
        });
        var onBehalf = await _world.Post(_world.Admin, "/api/tickets/on-behalf", new
        {
            clientEmail = _world.CliZ.Email,
            title = "Phone call",
            description = "<p>x</p>",
            source = "Phone",
            helpTopicId = topic,
            departmentId = _world.BranchA,
            slaPlanId = plan,
            priority = "Medium",
            assignedToUserId = _world.EmpB1.Id
        });
        var added = await _world.Post(_world.EmpA1, $"/api/tickets/{running}/assignees", new { userId = _world.EmpB1.Id });

        Assert.All(new[] { open, onBehalf, added }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Contains("branch", r.Text);
        });
        Assert.Equal(TicketStatus.New, await _world.Db(db => db.Tickets.Where(t => t.Id == fresh).Select(t => t.Status).SingleAsync()));
        Assert.False(await _world.Db(db => db.Tickets.AnyAsync(t => t.ClientId == _world.CliZ.Id)));
        Assert.False(await _world.Db(db => db.TicketAssignees.AnyAsync(a => a.TicketId == running && a.UserId == _world.EmpB1.Id)));
    }

    // ======================= TS-ONP-02.3: the same key ring and a different Pii:HashKey =======================

    [SqlFact]
    public async Task Starting_over_existing_accounts_with_a_different_Pii_HashKey_is_refused()
    {
        var protector = _world.Factory.Services.GetRequiredService<IPiiProtector>();
        var userId = _world.EmpA1.Id;
        var stored = await _world.Db(db => db.Database.SqlQuery<string>($"SELECT [NormalizedEmail] AS [Value] FROM [AspNetUsers] WHERE [Id] = {userId}").SingleAsync());
        Task<int> Run() => _world.Db(async db =>
        {
            await PiiBackfillMigration.RunAsync(db, protector, NullLogger.Instance);
            return 0;
        });

        await Run(); // the hashes in the database are what the key in use produces: nothing to say
        try
        {
            // What a different Pii:HashKey looks like from here: the stored lookup hash is not the one the current key makes.
            await _world.Db(db => db.Database.ExecuteSqlAsync($"UPDATE [AspNetUsers] SET [NormalizedEmail] = {new string('A', 64)} WHERE [Id] = {userId}"));

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(Run);

            Assert.Contains("Pii:HashKey", refused.Message);
            Assert.Contains("1 user(s)", refused.Message);
        }
        finally
        {
            await _world.Db(db => db.Database.ExecuteSqlAsync($"UPDATE [AspNetUsers] SET [NormalizedEmail] = {stored} WHERE [Id] = {userId}"));
        }
        await Run(); // and it was put back
    }

    // ======================= NOVO-PRV-01: a row that cannot be encrypted must not be left readable without anybody being told =======================

    [SqlFact]
    public async Task A_row_that_cannot_be_encrypted_stops_a_production_start_but_not_a_development_one()
    {
        var protector = _world.Factory.Services.GetRequiredService<IPiiProtector>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Task<int> Run(bool production) => _world.Db(async db =>
        {
            await PiiBackfillMigration.RunAsync(db, protector, NullLogger.Instance, refuseToStartWithPlaintextLeft: production);
            return 0;
        });
        try
        {
            // Two old accounts with the same user name, both with a plain-text address: encrypting the second collides with the first
            // (the user name is also kept as a lookup hash, and that is unique).
            foreach (var id in new[] { first, second })
                await _world.Db(db => db.Database.ExecuteSqlAsync($@"
                    INSERT INTO AspNetUsers (Id, FirstName, LastName, Role, IsActive, CreatedAtUtc, MustChangePassword, PhoneNumberPrompted, EmailConfirmed,
                                             PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, UserName, Email)
                    VALUES ({id}, 'Old', 'Account', 1, 1, SYSUTCDATETIME(), 0, 1, 1, 0, 0, 0, 0, 'old-duplicate-user', {id.ToString("N") + "@legacy.test"})"));

            await Run(production: false); // development: it says so in the log and carries on

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(production: true));
            Assert.Contains("plain text", refused.Message);
            Assert.Contains("1 row(s)", refused.Message);
        }
        finally
        {
            await _world.Db(db => db.Database.ExecuteSqlAsync($"DELETE FROM AspNetUsers WHERE Id IN ({first}, {second})"));
        }

        await Run(production: true); // and with the rows gone it is quiet again
    }

    private sealed class HostEnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
