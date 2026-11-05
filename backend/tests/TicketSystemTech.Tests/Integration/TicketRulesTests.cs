using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>Rules about tickets that the second analysis found missing (NOVO-DOM-07, NOVO-DOM-08, NOVO-PRV-02).</summary>
[Collection(ApiCollection.Name)]
public class TicketRulesTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();
    private readonly List<Guid> _lookups = new();

    public TicketRulesTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.TicketAttachments.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && _tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
            await db.HelpTopics.Where(h => _lookups.Contains(h.Id)).ExecuteDeleteAsync();
            return await db.SlaPlans.Where(p => _lookups.Contains(p.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> TicketAsync(TicketStatus status, Actor? assignee = null)
    {
        var ticket = new Ticket
        {
            TicketNumber = "TR-" + Guid.NewGuid().ToString("N")[..8],
            Title = "rules",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = _world.BranchA,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            OpenedAtUtc = status == TicketStatus.New ? null : DateTime.UtcNow.AddHours(-1),
            ClosedAtUtc = status == TicketStatus.Closed ? DateTime.UtcNow : null,
            ResolutionSummary = status == TicketStatus.Closed ? "done" : null
        };
        if (assignee is not null) ticket.Assignees.Add(new TicketAssignee { UserId = assignee.Id, IsPrimary = true });
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    [SqlFact]
    public async Task A_due_date_without_a_zone_means_UTC_whatever_zone_the_server_is_in()
    {
        var topic = new HelpTopic { Name = "TR " + Guid.NewGuid().ToString("N")[..6] };
        var plan = new SlaPlan { Name = "TR " + Guid.NewGuid().ToString("N")[..6], ResolutionTimeHours = 8 };
        await _world.Db(async db => { db.AddRange(topic, plan); return await db.SaveChangesAsync(); });
        _lookups.Add(topic.Id);
        _lookups.Add(plan.Id);
        var id = await TicketAsync(TicketStatus.New);
        var due = DateTime.UtcNow.AddDays(30).Date.AddHours(16);

        // The text carries no zone at all ("2031-10-25T16:00:00"); it used to be read in the zone of the machine.
        var opened = await _world.Post(_world.Admin, $"/api/tickets/{id}/open", new
        {
            source = "TicketSystem",
            helpTopicId = topic.Id,
            departmentId = _world.BranchA,
            subBranchId = (Guid?)null,
            slaPlanId = plan.Id,
            priority = "Medium",
            dueDateUtc = due.ToString("yyyy-MM-ddTHH:mm:ss"),
            assignedToUserId = _world.EmpA1.Id,
            categoryId = (Guid?)null
        });

        Assert.Equal(200, opened.Code);
        Assert.Equal(due, await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.DueDateUtc).SingleAsync()));
    }

    // NOVO-FE-05: a reply that only looks empty used to be saved and e-mailed to the client.
    [SqlFact]
    public async Task A_reply_or_a_description_that_only_looks_empty_is_refused()
    {
        var id = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);
        var before = await _world.Db(db => db.TicketMessages.CountAsync(m => m.TicketId == id));

        foreach (var body in new[] { "<p></p>", "<p> </p>", "<p>&nbsp;</p>", "<p><br></p>" })
        {
            var reply = await _world.Post(_world.EmpA1, $"/api/tickets/{id}/messages", new { type = "Response", bodyHtml = body });
            Assert.Equal(400, reply.Code);
        }
        Assert.Equal(before, await _world.Db(db => db.TicketMessages.CountAsync(m => m.TicketId == id)));

        var onBehalf = await _world.Post(_world.Admin, "/api/tickets/on-behalf", new
        {
            clientEmail = _world.CliX.Email,
            title = "prazan opis",
            description = "<p>&nbsp;</p>",
            source = "Phone",
            helpTopicId = Guid.NewGuid(),
            departmentId = _world.BranchA,
            slaPlanId = Guid.NewGuid(),
            priority = "Medium",
            assignedToUserId = _world.EmpA1.Id
        });
        Assert.Equal(400, onBehalf.Code);
        Assert.Contains("describe", onBehalf.Text);

        // and a reply with something in it, or only a picture, goes through
        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/messages", new { type = "Response", bodyHtml = "<p>Pozdrav</p>" })).Code);
        Assert.Equal(200, (await _world.Post(_world.EmpA1, $"/api/tickets/{id}/messages", new { type = "InternalNote", bodyHtml = "<p><img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==\"></p>" })).Code);
    }

    // NOVO-DOM-02: a ticket moved into a branch that has sub-branches used to end up in none of them, with no way to set one afterwards.
    [SqlFact]
    public async Task Moving_a_ticket_into_a_branch_with_sub_branches_needs_one_of_them()
    {
        var sub = new SubBranch { DepartmentId = _world.BranchB, Name = "TR sub " + Guid.NewGuid().ToString("N")[..6] };
        await _world.Db(async db => { db.SubBranches.Add(sub); return await db.SaveChangesAsync(); });
        var id = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);
        try
        {
            var without = await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB });
            Assert.Equal(400, without.Code);
            Assert.Contains("sub-branch", without.Text);
            Assert.Equal(_world.BranchA, await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.DepartmentId).SingleAsync())); // nothing moved

            var elsewhere = await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB, subBranchId = Guid.NewGuid() });
            Assert.Equal(400, elsewhere.Code); // a sub-branch that is not one of the destination's

            var moved = await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB, subBranchId = sub.Id });
            Assert.Equal(200, moved.Code);
            var department = await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.DepartmentId).SingleAsync());
            var subBranch = await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.SubBranchId).SingleAsync());
            Assert.Equal(_world.BranchB, department);
            Assert.Equal(sub.Id, subBranch);
        }
        finally
        {
            // The ticket points at the sub-branch, so it is let go first; and a branch must not keep one, or every other test that moves a ticket there would need it.
            await _world.Db(async db =>
            {
                await db.Tickets.Where(t => t.SubBranchId == sub.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.SubBranchId, (Guid?)null));
                return await db.SubBranches.Where(x => x.Id == sub.Id).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task Moving_a_ticket_into_a_branch_without_sub_branches_still_needs_none()
    {
        var id = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);

        var moved = await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB });

        Assert.Equal(200, moved.Code);
        Assert.Null(await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.SubBranchId).SingleAsync()));
    }

    [SqlFact]
    public async Task Who_worked_a_closed_ticket_cannot_be_taken_off_it()
    {
        var id = await TicketAsync(TicketStatus.Closed, _world.EmpA1);

        var removed = await _world.Send(_world.Admin, HttpMethod.Delete, $"/api/tickets/{id}/assignees/{_world.EmpA1.Id}");

        Assert.Equal(400, removed.Code);
        Assert.Single(await _world.Db(db => db.TicketAssignees.Where(a => a.TicketId == id).ToListAsync()));
    }

    [SqlFact]
    public async Task A_file_staff_put_on_the_ticket_itself_is_not_shown_to_the_client_but_the_clients_own_is()
    {
        var id = await TicketAsync(TicketStatus.InProgress, _world.EmpA1);
        await _world.Db(async db =>
        {
            db.TicketAttachments.Add(new TicketAttachment { TicketId = id, FileName = "interno.pdf", FileUrl = $"/uploads/{Guid.NewGuid()}.pdf", FileSizeBytes = 3, ContentType = "application/pdf", UploadedByUserId = _world.EmpA1.Id });
            db.TicketAttachments.Add(new TicketAttachment { TicketId = id, FileName = "moje.pdf", FileUrl = $"/uploads/{Guid.NewGuid()}.pdf", FileSizeBytes = 3, ContentType = "application/pdf", UploadedByUserId = _world.CliX.Id });
            return await db.SaveChangesAsync();
        });

        var asClient = await _world.Get(_world.CliX, $"/api/tickets/{id}");
        var asStaff = await _world.Get(_world.EmpA1, $"/api/tickets/{id}");

        Assert.Equal(200, asClient.Code);
        Assert.Equal(new[] { "moje.pdf" }, asClient.Json.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()).ToArray());
        Assert.Equal(new[] { "interno.pdf", "moje.pdf" }, asStaff.Json.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()).OrderBy(n => n).ToArray());
    }
}
