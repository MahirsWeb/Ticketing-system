using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// How many tickets of each status the summary and the branch breakdown count (T-37: the database counts them now, and what a count is
/// worth shows only when several tickets share a status). The tickets live in July 2036, a month no other test uses:
/// <code>
///   branch A  2 new, 1 open, 2 in progress, 1 resolved, 1 closed (closed in the range)
///   branch B  1 new
/// </code>
/// </summary>
[Collection(ApiCollection.Name)]
public class StatusCountTests : IAsyncLifetime
{
    private const string Range = "from=2036-07-01&to=2036-07-31";

    private readonly ApiWorld _world;
    private readonly List<Guid> _tickets = new();

    public StatusCountTests(ApiWorld world) => _world = world;

    private static DateTime July(int day) => new(2036, 7, day, 10, 0, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        if (!_world.Available) return;
        await AddAsync(_world.BranchA, TicketStatus.New, 1);
        await AddAsync(_world.BranchA, TicketStatus.New, 2);
        await AddAsync(_world.BranchA, TicketStatus.Open, 3);
        await AddAsync(_world.BranchA, TicketStatus.InProgress, 4);
        await AddAsync(_world.BranchA, TicketStatus.InProgress, 9);
        await AddAsync(_world.BranchA, TicketStatus.Resolved, 5);
        await AddAsync(_world.BranchA, TicketStatus.Closed, 6, closed: July(8));
        await AddAsync(_world.BranchB, TicketStatus.New, 7);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        await _world.Db(db => db.Tickets.Where(t => _tickets.Contains(t.Id)).ExecuteDeleteAsync());
    }

    private async Task AddAsync(Guid branch, TicketStatus status, int day, DateTime? closed = null)
    {
        var ticket = new Ticket
        {
            TicketNumber = "SC-" + Guid.NewGuid().ToString("N")[..8],
            Title = "status count",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = status,
            DepartmentId = branch,
            Priority = TicketPriority.Medium,
            Source = TicketSource.TicketSystem,
            CreatedAt = July(day),
            ClosedAtUtc = closed,
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        _tickets.Add(ticket.Id);
    }

    [SqlFact]
    public async Task The_summary_counts_every_ticket_of_a_status_and_puts_resolved_with_the_open_ones()
    {
        var summary = (await _world.Get(_world.EmpA1, $"/api/reports/summary?{Range}")).Json;

        Assert.Equal(2, summary.GetProperty("totalNew").GetInt32());
        Assert.Equal(4, summary.GetProperty("totalOpen").GetInt32()); // open, two in progress and resolved: resolved is not closed yet
        Assert.Equal(1, summary.GetProperty("totalClosed").GetInt32());
    }

    [SqlFact]
    public async Task The_branch_breakdown_counts_every_ticket_of_a_status_in_each_branch()
    {
        var rows = (await _world.Get(_world.Admin, $"/api/reports/by-branch?{Range}")).Json.EnumerateArray()
            .ToDictionary(r => r.GetProperty("departmentId").GetGuid(),
                r => (New: r.GetProperty("totalNew").GetInt32(), Open: r.GetProperty("totalOpen").GetInt32(), Closed: r.GetProperty("totalClosed").GetInt32()));

        Assert.Equal((2, 4, 1), rows[_world.BranchA]);
        Assert.Equal((1, 0, 0), rows[_world.BranchB]);
    }
}
