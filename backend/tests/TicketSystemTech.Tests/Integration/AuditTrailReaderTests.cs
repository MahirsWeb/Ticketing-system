using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Integration;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The reading of the audit trail for the reports. Above all: a "branch" in a transfer is a department (<c>Ticket.DepartmentId</c>), never a
/// sub-branch, which is only a finer unit inside a department.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuditTrailReaderTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();

    public AuditTrailReaderTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(async db =>
        {
            await db.TicketMessages.Where(m => _tickets.Contains(m.TicketId)).ExecuteDeleteAsync();
            await db.Notifications.Where(n => n.TicketId != null && _tickets.Contains(n.TicketId.Value)).ExecuteDeleteAsync();
            await db.TicketAssignees.Where(a => _tickets.Contains(a.TicketId)).ExecuteDeleteAsync();
            await db.AuditLogs.Where(a => a.EntityId != null && _tickets.Contains(a.EntityId.Value)).ExecuteDeleteAsync();
            return await db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync();
        });
    }

    private async Task<Guid> TicketAsync(Guid? department)
    {
        var ticket = new Ticket
        {
            TicketNumber = "AR-" + Guid.NewGuid().ToString("N")[..8],
            Title = "trail",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = TicketStatus.InProgress,
            DepartmentId = department,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            OpenedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
        return ticket.Id;
    }

    private Task<IReadOnlyList<AuditRow>> TrailAsync(Guid ticket) => _world.Db(db => new AuditTrailReader(db).ForTicketAsync(ticket, default));

    [SqlFact]
    public async Task A_branch_in_a_transfer_is_a_department_and_not_a_sub_branch()
    {
        var sub = new SubBranch { DepartmentId = _world.BranchB, Name = "AR sub " + Guid.NewGuid().ToString("N")[..6] };
        await _world.Db(async db => { db.SubBranches.Add(sub); return await db.SaveChangesAsync(); });
        var id = await TicketAsync(_world.BranchA);
        try
        {
            Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB, subBranchId = sub.Id })).Code);

            var row = Assert.Single(await TrailAsync(id), r => r.Action == "TicketTransferred");
            var transfer = TicketAuditDetails.Transfer(row.Details);

            Assert.Equal((_world.BranchA, _world.BranchB), transfer);
            Assert.DoesNotContain(sub.Id.ToString(), row.Details); // the finer unit is not in the words
            Assert.Equal(_world.BranchB, await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.DepartmentId).SingleAsync()));
            Assert.Equal(sub.Id, await _world.Db(db => db.Tickets.Where(t => t.Id == id).Select(t => t.SubBranchId).SingleAsync()));
            Assert.Contains(await _world.Db(db => db.Departments.Select(d => d.Id).ToListAsync()), d => d == transfer!.Value.To);
            Assert.DoesNotContain(await _world.Db(db => db.SubBranches.Select(s => s.Id).ToListAsync()), s => s == transfer!.Value.To);
        }
        finally
        {
            await _world.Db(async db =>
            {
                await db.Tickets.Where(t => t.SubBranchId == sub.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.SubBranchId, (Guid?)null));
                return await db.SubBranches.Where(x => x.Id == sub.Id).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task A_ticket_without_a_branch_is_moved_from_none()
    {
        var id = await TicketAsync(null);

        Assert.Equal(200, (await _world.Post(_world.Admin, $"/api/tickets/{id}/transfer-branch", new { departmentId = _world.BranchB })).Code);

        var row = Assert.Single(await TrailAsync(id));
        Assert.Equal((null, _world.BranchB), TicketAuditDetails.Transfer(row.Details));
    }

    [SqlFact]
    public async Task The_trail_of_a_ticket_has_only_the_rows_of_its_life_oldest_first()
    {
        var id = await TicketAsync(_world.BranchA);
        var other = await TicketAsync(_world.BranchA);
        var t = DateTime.UtcNow.AddMinutes(-30);
        await _world.Db(async db =>
        {
            db.AuditLogs.AddRange(
                new AuditLog { Action = "TicketStatusChanged", EntityType = nameof(Ticket), EntityId = id, Details = "Open -> InProgress", CreatedAt = t.AddMinutes(5) },
                new AuditLog { Action = "TicketOpened", EntityType = nameof(Ticket), EntityId = id, Details = "x", CreatedAt = t },
                new AuditLog { Action = "TicketViewed", EntityType = nameof(Ticket), EntityId = id, CreatedAt = t.AddMinutes(1) }, // not an event of its life
                new AuditLog { Action = "TicketOpened", EntityType = "User", EntityId = id, CreatedAt = t }, // another kind of entity with the same id
                new AuditLog { Action = "TicketOpened", EntityType = nameof(Ticket), EntityId = other, CreatedAt = t }); // another ticket
            return await db.SaveChangesAsync();
        });

        var trail = await TrailAsync(id);

        Assert.Equal(new[] { "TicketOpened", "TicketStatusChanged" }, trail.Select(r => r.Action));
        await _world.Db(db => db.AuditLogs.Where(a => a.EntityType == "User" && a.EntityId == id).ExecuteDeleteAsync());
    }

    [SqlFact]
    public async Task The_transfers_of_a_period_are_those_inside_it_and_only_the_understood_ones()
    {
        var id = await TicketAsync(_world.BranchA);
        var t = DateTime.UtcNow.AddDays(-3);
        await _world.Db(async db =>
        {
            db.AuditLogs.AddRange(
                new AuditLog { Action = "TicketTransferred", EntityType = nameof(Ticket), EntityId = id, Details = TicketAuditDetails.Transferred(_world.BranchA, _world.BranchB), CreatedAt = t },
                new AuditLog { Action = "TicketTransferred", EntityType = nameof(Ticket), EntityId = id, Details = TicketAuditDetails.Transferred(null, _world.BranchA), CreatedAt = t.AddDays(-10) }, // before
                new AuditLog { Action = "TicketTransferred", EntityType = nameof(Ticket), EntityId = id, Details = "Branch a -> b", CreatedAt = t.AddHours(1) }, // not understood
                new AuditLog { Action = "TicketClosed", EntityType = nameof(Ticket), EntityId = id, Details = TicketAuditDetails.Transferred(_world.BranchA, _world.BranchB), CreatedAt = t }); // other kind of row
            return await db.SaveChangesAsync();
        });

        var rows = (await _world.Db(db => new AuditTrailReader(db).TransfersAsync(t.AddDays(-1), t.AddDays(1), default))).Where(r => r.TicketId == id).ToList();

        var row = Assert.Single(rows);
        Assert.Equal((_world.BranchA, _world.BranchB), (row.FromDepartmentId, row.ToDepartmentId));
    }
}
