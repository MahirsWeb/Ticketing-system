using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// Retest findings about what is recorded and what is not waited for: the audit trail of sensitive actions (TS-10.2), closing a
/// ticket without waiting for the AI provider (TS-HOST-03.1) and one answer for "no mailbox is connected" (TS-16.1).
/// </summary>
[Collection(ApiCollection.Name)]
public class AuditAndBackgroundWorkTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _users = new();
    private readonly List<Guid> _departments = new();

    public AuditAndBackgroundWorkTests(ApiWorld world) => _world = world;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_world.Available) return;
        _world.Factory.Embeddings.Delay = TimeSpan.Zero;
        await _world.Db(async db =>
        {
            await db.KnowledgeBaseChunks.Where(c => c.TicketId != null && _tickets.Contains(c.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && _tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(a => a.EntityId != null && (_users.Contains(a.EntityId.Value) || _tickets.Contains(a.EntityId.Value) || _departments.Contains(a.EntityId.Value))).ExecuteDeleteAsync();
            await db.Users.Where(u => _users.Contains(u.Id)).ExecuteDeleteAsync();
            return await db.Departments.Where(d => _departments.Contains(d.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> NewTicketAsync(TicketStatus status, Guid? branch = null, Action<Ticket>? customise = null)
    {
        var ticket = new Ticket
        {
            TicketNumber = "AU-" + Guid.NewGuid().ToString("N")[..8],
            Title = "audit",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = branch ?? _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            AssignedToUserId = _world.EmpA1.Id,
            OpenedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        customise?.Invoke(ticket);
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private Task<List<AuditLog>> AuditOfAsync(Guid entity) =>
        _world.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == entity).OrderBy(a => a.CreatedAt).ToListAsync());

    private async Task AssertAuditedAsync(Guid entity, string action, Actor actor)
    {
        var rows = await AuditOfAsync(entity);
        var row = Assert.Single(rows, r => r.Action == action);
        Assert.Equal(actor.Id, row.UserId);
        Assert.True(Math.Abs((DateTime.UtcNow - row.CreatedAt).TotalMinutes) < 5, "the row says when it happened");
        Assert.False(string.IsNullOrWhiteSpace(row.CorrelationId), "and which request caused it");
    }

    // ======================= TS-10.2: the audit trail =======================

    [SqlFact]
    public async Task Creating_deactivating_and_changing_an_account_leaves_a_row_for_each_with_who_and_which_request()
    {
        var created = await _world.Post(_world.Admin, "/api/users/employees", new
        {
            firstName = "Audit",
            lastName = "Subject",
            email = $"audit-{Guid.NewGuid():N}@authz.test",
            role = "Employee",
            departmentId = _world.BranchA,
            subBranchId = (Guid?)null
        });
        Assert.Equal(200, created.Code);
        var id = created.Json.GetProperty("userId").GetGuid();
        _users.Add(id);
        await AssertAuditedAsync(id, "UserCreated", _world.Admin);

        // the row carries the same reference the response carried, which is also on every log line of that request
        var row = Assert.Single(await AuditOfAsync(id));
        Assert.Equal(created.Header("X-Correlation-Id"), row.CorrelationId);

        var update = await _world.Send(_world.Admin, HttpMethod.Put, $"/api/users/{id}", new { firstName = "Audit", lastName = "Changed", role = "Employee", companyId = (Guid?)null, departmentId = _world.BranchB, subBranchId = (Guid?)null });
        Assert.Equal(200, update.Code);
        await AssertAuditedAsync(id, "UserUpdated", _world.Admin);

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{id}/active", new { isActive = false })).Code);
        await AssertAuditedAsync(id, "UserDeactivated", _world.Admin);
        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{id}/active", new { isActive = true })).Code);
        await AssertAuditedAsync(id, "UserActivated", _world.Admin);
    }

    [SqlFact]
    public async Task Opening_closing_reopening_transferring_and_changing_the_status_of_a_ticket_are_all_recorded()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);

        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/close", new { resolutionSummary = "done" })).Code);
        await AssertAuditedAsync(ticket, "TicketClosed", _world.EmpA1);

        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/reopen")).Code);
        await AssertAuditedAsync(ticket, "TicketReopened", _world.EmpA1);

        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/status", new { status = "Resolved" })).Code);
        await AssertAuditedAsync(ticket, "TicketStatusChanged", _world.EmpA1);

        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/transfer-branch", new { departmentId = _world.BranchB })).Code);
        await AssertAuditedAsync(ticket, "TicketTransferred", _world.EmpA1);
    }

    [SqlFact]
    public async Task Opening_a_ticket_and_changing_who_works_on_it_are_recorded()
    {
        var topic = new HelpTopic { Name = "Audit " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "Audit " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db => { db.AddRange(topic, plan); return await db.SaveChangesAsync(); });
        try
        {
            var ticket = await NewTicketAsync(TicketStatus.New);
            var opened = await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/open", new
            {
                source = "TicketSystem",
                helpTopicId = topic.Id,
                departmentId = _world.BranchA,
                subBranchId = (Guid?)null,
                slaPlanId = plan.Id,
                priority = "Medium",
                dueDateUtc = (DateTime?)null,
                assignedToUserId = _world.EmpA1.Id,
                categoryId = (Guid?)null
            });
            Assert.Equal(200, opened.Code);
            await AssertAuditedAsync(ticket, "TicketOpened", _world.EmpA1);

            Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/assignees", new { userId = _world.EmpA2.Id })).Code);
            await AssertAuditedAsync(ticket, "TicketAssigneeAdded", _world.EmpA1);
            Assert.Equal(200, (await _world.Send(_world.EmpA1, HttpMethod.Delete, $"/api/tickets/{ticket}/assignees/{_world.EmpA2.Id}")).Code);
            await AssertAuditedAsync(ticket, "TicketAssigneeRemoved", _world.EmpA1);
        }
        finally
        {
            await _world.Db(async db =>
            {
                await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.HelpTopicId, (Guid?)null).SetProperty(t => t.SlaPlanId, (Guid?)null));
                await db.HelpTopics.Where(h => h.Id == topic.Id).ExecuteDeleteAsync();
                return await db.SlaPlans.Where(p => p.Id == plan.Id).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task Branches_being_created_changed_and_deleted_are_recorded()
    {
        var email = $"audit-{Guid.NewGuid():N}@branch.test";
        var created = await _world.Post(_world.Admin, "/api/departments", new { name = "Audit Branch", email });
        Assert.Equal(200, created.Code);
        var id = created.Json.GetProperty("id").GetGuid();
        _departments.Add(id);
        await AssertAuditedAsync(id, "DepartmentCreated", _world.Admin);

        Assert.Equal(200, (await _world.Send(_world.Admin, HttpMethod.Put, $"/api/departments/{id}", new { name = "Audit Branch 2", email, isActive = false })).Code);
        await AssertAuditedAsync(id, "DepartmentUpdated", _world.Admin);

        Assert.Equal(204, (await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/departments/{id}")).Code);
        await AssertAuditedAsync(id, "DepartmentDeleted", _world.Admin);
    }

    [SqlFact]
    public async Task What_is_written_to_the_trail_never_contains_a_password_or_a_message_text()
    {
        var created = await _world.Post(_world.Admin, "/api/users/employees", new
        {
            firstName = "Audit",
            lastName = "Secret",
            email = $"audit-{Guid.NewGuid():N}@authz.test",
            role = "Employee",
            departmentId = _world.BranchA,
            subBranchId = (Guid?)null
        });
        var id = created.Json.GetProperty("userId").GetGuid();
        _users.Add(id);
        var tempPassword = created.Json.GetProperty("temporaryPassword").GetString()!;

        var rows = await AuditOfAsync(id);

        Assert.All(rows, r => Assert.DoesNotContain(tempPassword, r.Details ?? ""));
        Assert.All(rows, r => Assert.DoesNotContain("authz.test", r.Details ?? "")); // not even the address
    }

    [SqlFact]
    public async Task The_address_of_the_request_is_stamped_on_a_row_wherever_in_the_application_it_was_added()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { TraceIdentifier = "ref-123456" } };
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

        var entity = Guid.NewGuid();
        using var scope = _world.Factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using (var db = new AppDbContext(options, scope.ServiceProvider.GetRequiredService<IPiiProtector>(), accessor))
        {
            db.Audit(_world.Admin.Id, "ProbeAction", "Probe", entity, "first");
            await db.SaveChangesAsync();
        }
        await using (var outside = new AppDbContext(options, scope.ServiceProvider.GetRequiredService<IPiiProtector>())) // no request at all
        {
            outside.Audit(_world.Admin.Id, "ProbeAction", "Probe", entity, "second");
            await outside.SaveChangesAsync();
        }

        try
        {
            var rows = await AuditOfAsync(entity);
            var first = Assert.Single(rows, r => r.Details == "first");
            var second = Assert.Single(rows, r => r.Details == "second");
            Assert.Equal(("203.0.113.9", "ref-123456"), (first.IpAddress, first.CorrelationId));
            Assert.Equal((null, null), (second.IpAddress, second.CorrelationId));
        }
        finally
        {
            await _world.Db(db => db.AuditLogs.Where(a => a.EntityId == entity).ExecuteDeleteAsync());
        }
    }

    // ======================= TS-HOST-03.1: not waiting for the AI provider =======================

    private async Task<Guid> TicketReadyToCloseAsync()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);
        await _world.Db(async db =>
        {
            db.TicketMessages.Add(new TicketMessage { TicketId = ticket, AuthorId = _world.EmpA1.Id, Type = MessageType.InternalNote, BodyHtml = new string('n', 200) });
            return await db.SaveChangesAsync();
        });
        return ticket;
    }

    private async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 10)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < limit)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("the condition did not become true in time");
    }

    [SqlFact]
    public async Task Closing_a_ticket_is_answered_at_once_even_when_the_AI_provider_takes_seconds_and_the_ticket_is_indexed_afterwards()
    {
        var ticket = await TicketReadyToCloseAsync();
        _world.Factory.Embeddings.Delay = TimeSpan.FromSeconds(3);
        var started = DateTime.UtcNow;

        var closed = await _world.Post(_world.EmpA1, $"/api/tickets/{ticket}/close", new { resolutionSummary = "TST zatvaranje za HOST-03" });

        var took = DateTime.UtcNow - started;
        Assert.Equal(200, closed.Code);
        Assert.True(took < TimeSpan.FromSeconds(2), $"closing took {took.TotalSeconds:0.0} s");
        Assert.Equal(TicketStatus.Closed, await _world.Db(db => db.Tickets.Where(t => t.Id == ticket).Select(t => t.Status).SingleAsync()));

        // ...and a moment later the ticket is searchable
        await WaitUntilAsync(async () => await _world.Db(db => db.KnowledgeBaseChunks.AnyAsync(c => c.TicketId == ticket && c.Embedding != null)));
    }

    [SqlFact]
    public async Task A_note_added_to_a_ticket_that_is_past_triage_is_answered_at_once_too()
    {
        var ticket = await NewTicketAsync(TicketStatus.Resolved);
        _world.Factory.Embeddings.Delay = TimeSpan.FromSeconds(3);
        var started = DateTime.UtcNow;

        var added = await _world.Send(_world.EmpA1, HttpMethod.Post, $"/api/tickets/{ticket}/messages", new { type = "InternalNote", bodyHtml = "<p>" + new string('n', 200) + "</p>" });

        Assert.Equal(200, added.Code);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        await WaitUntilAsync(async () => await _world.Db(db => db.KnowledgeBaseChunks.AnyAsync(c => c.TicketId == ticket)));
    }

    [SqlFact]
    public async Task A_ticket_asked_for_several_times_while_it_waits_is_indexed_once()
    {
        var busy = await TicketReadyToCloseAsync();
        var waiting = await TicketReadyToCloseAsync();
        await _world.Db(db => db.Tickets.Where(t => t.Id == busy || t.Id == waiting).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TicketStatus.Closed)));
        _world.Factory.Embeddings.Delay = TimeSpan.FromMilliseconds(500);
        var queue = _world.Factory.Services.GetRequiredService<IKnowledgeBaseIndexQueue>();
        var before = _world.Factory.Embeddings.Calls;

        queue.Enqueue(busy);
        await Task.Delay(150); // the worker is now inside the slow call for `busy`
        queue.Enqueue(waiting);
        queue.Enqueue(waiting);
        queue.Enqueue(waiting);

        await WaitUntilAsync(async () => await _world.Db(db => db.KnowledgeBaseChunks.CountAsync(c => c.TicketId == busy || c.TicketId == waiting)) == 2);
        await Task.Delay(800);
        Assert.Equal(2, _world.Factory.Embeddings.Calls - before);
    }

    // ======================= TS-16.1: no mailbox connected =======================

    [SqlFact]
    public async Task Every_action_that_needs_the_mailbox_answers_the_same_way_when_none_is_connected()
    {
        var ticket = await NewTicketAsync(TicketStatus.InProgress);
        var answers = new[]
        {
            await _world.Get(_world.EmpA1, "/api/email-integration/messages"),
            await _world.Get(_world.EmpA1, "/api/email-integration/messages/abc/prefill"),
            await _world.Post(_world.EmpA1, $"/api/email-integration/messages/abc/complete-ticket?ticketId={ticket}")
        };

        Assert.All(answers, a =>
        {
            Assert.Equal(409, a.Code);
            Assert.Equal("MAILBOX_NOT_CONNECTED", a.Json.GetProperty("code").GetString());
            Assert.Contains("No mailbox is connected", a.Json.GetProperty("message").GetString());
        });
    }

    [Fact]
    public async Task The_translation_to_409_is_made_in_one_place()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-mb" };
        context.Response.Body = new MemoryStream();
        await new ApiExceptionMiddleware(_ => throw new MailboxNotConnectedException(), NullLogger<ApiExceptionMiddleware>.Instance, new HostEnvironmentStub())
            .InvokeAsync(context);

        context.Response.Body.Position = 0;
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("MAILBOX_NOT_CONNECTED", JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("code").GetString());
    }

    private sealed class HostEnvironmentStub : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
