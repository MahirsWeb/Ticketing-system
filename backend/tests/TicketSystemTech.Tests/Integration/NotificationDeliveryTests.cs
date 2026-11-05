using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// T-47: <c>NotifierTests</c> pin the words; these pin that every place in the application that changes something also says so.
/// Each test makes the real change through the API and then reads what the person was given: a line in their list (saved with the change)
/// and an e-mail filed in the outbox. Before the notifications were gathered into one place, nothing checked either: a service that forgot
/// to tell anybody kept all 706 tests green. (A push cannot be seen from here, because nobody is connected; <c>NotifierTests</c> cover it.)
/// </summary>
[Collection(ApiCollection.Name)]
public class NotificationDeliveryTests : IAsyncLifetime
{
    private const string Marker = "NOTIFY-";

    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _tasks = new();
    private readonly List<Guid> _lookups = new();
    private (Guid Topic, Guid Plan)? _routing;

    public NotificationDeliveryTests(ApiWorld world) => _world = world;

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(db => db.EmailOutbox.ExecuteDeleteAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => (n.TicketId != null && _tickets.Contains(n.TicketId.Value)) || n.Message.Contains(Marker)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.WorkTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
            await db.SlaPlans.Where(p => _lookups.Contains(p.Id)).ExecuteDeleteAsync();
            return await db.EmailOutbox.ExecuteDeleteAsync();
        });
    }

    private sealed record Made(Guid Id, string Number);

    private async Task<Made> TicketAsync(TicketStatus status, Actor? assignee = null)
    {
        var ticket = new Ticket
        {
            TicketNumber = "NT-" + Guid.NewGuid().ToString("N")[..8],
            Title = Marker + "ticket",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            OpenedAtUtc = status == TicketStatus.New ? null : DateTime.UtcNow.AddHours(-1)
        };
        if (assignee is not null) ticket.Assignees.Add(new TicketAssignee { UserId = assignee.Id, IsPrimary = true });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return new Made(ticket.Id, ticket.TicketNumber);
    }

    private Made Track(ApiResponse created)
    {
        Assert.Equal(200, created.Code);
        var made = new Made(created.Json.GetProperty("id").GetGuid(), created.Json.GetProperty("ticketNumber").GetString()!);
        _tickets.Add(made.Id);
        return made;
    }

    private async Task<(Guid Topic, Guid Plan)> RoutingAsync()
    {
        if (_routing is { } existing) return existing;
        var topic = new HelpTopic { Name = "NT " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "NT " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db => { db.AddRange(topic, plan); return await db.SaveChangesAsync(); });
        _lookups.Add(topic.Id);
        _lookups.Add(plan.Id);
        _routing = (topic.Id, plan.Id);
        return _routing.Value;
    }

    /// <summary>What is in the person's list for this ticket: the kind and the words.</summary>
    private async Task<List<(NotificationType, string)>> LinesAsync(Actor who, Guid ticketId) =>
        (await _world.Db(db => db.Notifications.AsNoTracking().Where(n => n.UserId == who.Id && n.TicketId == ticketId).ToListAsync()))
            .Select(n => (n.Type, n.Message)).ToList();

    /// <summary>The e-mails filed for the person since the test began.</summary>
    private async Task<List<EmailOutboxItem>> MailsAsync(Actor who) =>
        (await _world.Db(db => db.EmailOutbox.AsNoTracking().ToListAsync())).Where(m => m.ToEmail == who.Email).ToList();

    private async Task AssertToldAsync(Actor who, Made ticket, NotificationType type, string line, string subject)
    {
        Assert.Equal(new[] { (type, line) }, await LinesAsync(who, ticket.Id));
        Assert.Equal(new[] { subject }, (await MailsAsync(who)).Select(m => m.Subject));
    }

    private async Task AssertNotToldAsync(Actor who, Made ticket)
    {
        Assert.Empty(await LinesAsync(who, ticket.Id));
        Assert.Empty(await MailsAsync(who));
    }

    [SqlFact]
    public async Task A_client_submitting_a_ticket_tells_the_staff_of_the_branch_it_went_to_and_nobody_else()
    {
        var created = await _world.Post(_world.CliX, "/api/tickets", new { title = Marker + "submitted", description = "<p>Printer ne radi</p>", departmentId = _world.BranchA });
        var ticket = Track(created);

        foreach (var staff in new[] { _world.EmpA1, _world.EmpA2 })
            await AssertToldAsync(staff, ticket, NotificationType.NewTicket, $"New ticket #{ticket.Number}: {Marker}submitted", $"New ticket #{ticket.Number} submitted — Acme");
        await AssertNotToldAsync(_world.EmpB1, ticket);
        await AssertNotToldAsync(_world.CliX, ticket);
    }

    [SqlFact]
    public async Task Opening_a_ticket_for_a_client_by_phone_tells_the_assignee_and_the_client()
    {
        var (topic, plan) = await RoutingAsync();

        var created = await _world.Post(_world.Admin, "/api/tickets/on-behalf", new
        {
            clientEmail = _world.CliX.Email,
            title = Marker + "phone",
            description = "<p>x</p>",
            source = "Phone",
            helpTopicId = topic,
            departmentId = _world.BranchA,
            slaPlanId = plan,
            priority = "Medium",
            assignedToUserId = _world.EmpA1.Id
        });
        var ticket = Track(created);

        await AssertToldAsync(_world.EmpA1, ticket, NotificationType.TicketAssigned, $"Ticket #{ticket.Number} assigned to you", $"Ticket #{ticket.Number} assigned to you");
        await AssertToldAsync(_world.CliX, ticket, NotificationType.TicketOpened, $"Your ticket #{ticket.Number} has been opened", $"Your ticket #{ticket.Number} has been opened");
        await AssertNotToldAsync(_world.EmpA2, ticket);
    }

    [SqlFact]
    public async Task Opening_a_submitted_ticket_tells_the_assignee_and_the_client()
    {
        var (topic, plan) = await RoutingAsync();
        var ticket = await TicketAsync(TicketStatus.New);

        var opened = await _world.Post(_world.Admin, $"/api/tickets/{ticket.Id}/open", new
        {
            source = "TicketSystem",
            helpTopicId = topic,
            departmentId = _world.BranchA,
            slaPlanId = plan,
            priority = "Medium",
            assignedToUserId = _world.EmpA1.Id
        });

        Assert.Equal(200, opened.Code);
        await AssertToldAsync(_world.EmpA1, ticket, NotificationType.TicketAssigned, $"Ticket #{ticket.Number} assigned to you", $"Ticket #{ticket.Number} assigned to you");
        await AssertToldAsync(_world.CliX, ticket, NotificationType.TicketOpened, $"Your ticket #{ticket.Number} has been opened", $"Your ticket #{ticket.Number} has been opened");
        await AssertNotToldAsync(_world.EmpA2, ticket);
    }

    [SqlFact]
    public async Task Moving_a_ticket_to_another_branch_tells_the_staff_of_that_branch_where_it_came_from()
    {
        var ticket = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);

        var moved = await _world.Post(_world.Admin, $"/api/tickets/{ticket.Id}/transfer-branch", new { departmentId = _world.BranchB });

        Assert.Equal(200, moved.Code);
        await AssertToldAsync(_world.EmpB1, ticket, NotificationType.TicketTransferred, $"Ticket #{ticket.Number} transferred to Branch B", $"Ticket #{ticket.Number} transferred to your branch");
        Assert.Contains($"from <b>Branch A</b> to <b>Branch B</b> by {_world.Admin.Key} Tester", Assert.Single(await MailsAsync(_world.EmpB1)).HtmlBody);
        await AssertNotToldAsync(_world.EmpA1, ticket); // the branch it left is not told it arrived somewhere else
    }

    [SqlFact]
    public async Task Closing_a_ticket_tells_its_client_it_has_been_resolved_and_by_whom()
    {
        var ticket = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);

        var closed = await _world.Post(_world.Admin, $"/api/tickets/{ticket.Id}/close", new { resolutionSummary = "Zamijenjen toner" });

        Assert.Equal(200, closed.Code);
        await AssertToldAsync(_world.CliX, ticket, NotificationType.TicketClosed, $"Ticket #{ticket.Number} has been resolved", $"Your ticket #{ticket.Number} has been resolved");
        Assert.Contains($"{_world.Admin.Key} Tester", Assert.Single(await MailsAsync(_world.CliX)).HtmlBody);
        await AssertNotToldAsync(_world.EmpA1, ticket);
    }

    [SqlFact]
    public async Task Adding_somebody_to_a_ticket_tells_that_person_and_not_the_ones_already_on_it()
    {
        var ticket = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);

        var added = await _world.Post(_world.Admin, $"/api/tickets/{ticket.Id}/assignees", new { userId = _world.EmpA2.Id });

        Assert.Equal(200, added.Code);
        await AssertToldAsync(_world.EmpA2, ticket, NotificationType.TicketAssigned, $"Ticket #{ticket.Number} assigned to you", $"Ticket #{ticket.Number} assigned to you");
        await AssertNotToldAsync(_world.EmpA1, ticket);
    }

    [SqlFact]
    public async Task A_reply_tells_the_other_side_staff_to_the_client_and_the_client_to_everybody_working_on_it()
    {
        var ticket = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);
        var reply = $"New reply on ticket #{ticket.Number}";

        var fromStaff = await _world.Post(_world.EmpA1, $"/api/tickets/{ticket.Id}/messages", new { type = "Response", bodyHtml = "<p>Pogledali smo.</p>" });

        Assert.Equal(200, fromStaff.Code);
        await AssertToldAsync(_world.CliX, ticket, NotificationType.NewResponse, reply, reply);
        await AssertNotToldAsync(_world.EmpA1, ticket); // the author is not told about their own words

        var fromClient = await _world.Post(_world.CliX, $"/api/tickets/{ticket.Id}/messages", new { type = "Response", bodyHtml = "<p>Hvala.</p>" });

        Assert.Equal(200, fromClient.Code);
        await AssertToldAsync(_world.EmpA1, ticket, NotificationType.NewResponse, reply, reply);
        await AssertNotToldAsync(_world.EmpA2, ticket);
    }

    [SqlFact]
    public async Task An_internal_note_tells_nobody()
    {
        var ticket = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);

        var note = await _world.Post(_world.EmpA1, $"/api/tickets/{ticket.Id}/messages", new { type = "InternalNote", bodyHtml = "<p>Samo za nas.</p>" });

        Assert.Equal(200, note.Code);
        await AssertNotToldAsync(_world.CliX, ticket);
        await AssertNotToldAsync(_world.EmpA1, ticket);
    }

    [SqlFact]
    public async Task Handing_a_task_to_somebody_tells_them_without_pointing_at_a_ticket_and_so_does_passing_it_on()
    {
        var title = Marker + "task " + Guid.NewGuid().ToString("N")[..6];
        var created = await _world.Post(_world.Admin, "/api/work-tasks", new { title, description = "d", assignedToUserId = _world.EmpA1.Id });
        Assert.Equal(200, created.Code);
        var taskId = created.Json.GetProperty("id").GetGuid();
        _tasks.Add(taskId);

        var lines = await _world.Db(db => db.Notifications.AsNoTracking().Where(n => n.UserId == _world.EmpA1.Id && n.Message.Contains(title)).ToListAsync());
        var line = Assert.Single(lines);
        Assert.Equal((NotificationType.TaskAssigned, $"{_world.Admin.Key} Tester assigned you a task: \"{title}\""), (line.Type, line.Message));
        Assert.Null(line.TicketId);
        Assert.Equal(new[] { "A task has been assigned to you" }, (await MailsAsync(_world.EmpA1)).Select(m => m.Subject));

        // Only an employee passes a task on (an administrator may hand one out but not pass it on), so the one who has it does.
        var passedOn = await _world.Post(_world.EmpA1, $"/api/work-tasks/{taskId}/reassign", new { assignedToUserId = _world.EmpA2.Id });

        Assert.Equal(200, passedOn.Code);
        var second = Assert.Single(await _world.Db(db => db.Notifications.AsNoTracking().Where(n => n.UserId == _world.EmpA2.Id && n.Message.Contains(title)).ToListAsync()));
        Assert.Equal((NotificationType.TaskAssigned, $"{_world.EmpA1.Key} Tester assigned you a task: \"{title}\""), (second.Type, second.Message));
        Assert.Equal(new[] { "A task has been assigned to you" }, (await MailsAsync(_world.EmpA2)).Select(m => m.Subject));
    }
}
