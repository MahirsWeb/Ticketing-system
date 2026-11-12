using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Integration;

/// <summary>
/// What another application reads. Two questions: "who was put on a ticket since I last asked" (the feed, for the application that asks every
/// minute or so) and "which tickets is this person on now" (the picture, to start from and to check against, because the feed cannot tell
/// that somebody was taken off a ticket). Both see every branch: a key is not a person and has no branch.
/// </summary>
public interface IIntegrationQueries
{
    Task<Outcome<IntegrationAssignmentPage>> AssignmentsAsync(string? since, Guid? afterId, string? email, int limit, CancellationToken ct);

    Task<Outcome<IntegrationTicketPage>> TicketsAsync(string? assigneeEmail, string? state, int page, int pageSize, CancellationToken ct);
}

public sealed class IntegrationQueries : IIntegrationQueries
{
    public const int MaxAssignmentsPerPage = 500;
    public const int MaxTicketsPerPage = 100;

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _configuration;

    public IntegrationQueries(AppDbContext db, UserManager<ApplicationUser> users, IConfiguration configuration)
    {
        _db = db;
        _users = users;
        _configuration = configuration;
    }

    public async Task<Outcome<IntegrationAssignmentPage>> AssignmentsAsync(string? since, Guid? afterId, string? email, int limit, CancellationToken ct)
    {
        if (!TryParseUtc(since, out var from))
            return Outcome<IntegrationAssignmentPage>.Invalid("since is required: a UTC time such as 2026-01-01T00:00:00Z.");
        limit = Math.Clamp(limit, 1, MaxAssignmentsPerPage);

        Guid? personId = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            personId = await FindUserIdAsync(email, ct);
            if (personId is null) return Outcome<IntegrationAssignmentPage>.Ok(new IntegrationAssignmentPage([], from, afterId, false)); // nobody has this address: nothing to report
        }

        var query = _db.TicketAssignees.AsNoTracking().AsQueryable();
        if (personId.HasValue) query = query.Where(a => a.UserId == personId.Value);

        // Time alone cannot say where a page ended when several people were put on tickets in the same instant (a bulk change): the id
        // breaks the tie. Without afterId the answer starts AT the time, so a caller that keeps only the time may see the last item
        // twice but never misses one; the id of every item is there to recognise it.
        query = afterId.HasValue
            ? query.Where(a => a.AssignedAtUtc > from || (a.AssignedAtUtc == from && a.Id.CompareTo(afterId.Value) > 0))
            : query.Where(a => a.AssignedAtUtc >= from);

        var rows = await query.OrderBy(a => a.AssignedAtUtc).ThenBy(a => a.Id).Take(limit + 1)
            .Select(a => new { a.Id, a.TicketId, a.UserId, a.AssignedAtUtc, a.IsPrimary })
            .ToListAsync(ct);
        var hasMore = rows.Count > limit;
        rows = rows.Take(limit).ToList();

        var built = await BuildAsync(rows.Select(r => r.TicketId).Distinct().ToList(), ct);
        var items = rows
            .Where(r => built.Tickets.ContainsKey(r.TicketId) && built.People.ContainsKey(r.UserId))
            .Select(r => new IntegrationAssignment(r.Id, r.AssignedAtUtc, r.IsPrimary, built.People[r.UserId], built.Tickets[r.TicketId]))
            .ToList();

        var last = rows.Count > 0 ? rows[^1] : null;
        return Outcome<IntegrationAssignmentPage>.Ok(new IntegrationAssignmentPage(items, last?.AssignedAtUtc ?? from, last?.Id ?? afterId, hasMore));
    }

    public async Task<Outcome<IntegrationTicketPage>> TicketsAsync(string? assigneeEmail, string? state, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxTicketsPerPage);
        var wanted = (state ?? "active").Trim().ToLowerInvariant();
        if (wanted is not ("active" or "closed" or "all"))
            return Outcome<IntegrationTicketPage>.Invalid("state must be active (being worked on), closed or all.");

        var query = _db.Tickets.AsNoTracking().AsQueryable();
        query = wanted switch
        {
            "active" => query.Where(t => TicketWorkflow.ActiveStatuses.Contains(t.Status)),
            "closed" => query.Where(t => t.Status == TicketStatus.Closed),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(assigneeEmail))
        {
            var personId = await FindUserIdAsync(assigneeEmail, ct);
            if (personId is null) return Outcome<IntegrationTicketPage>.Ok(new IntegrationTicketPage([], page, pageSize, 0));
            query = query.Where(t => t.Assignees.Any(a => a.UserId == personId.Value));
        }

        var total = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(t => t.Id).ToListAsync(ct);

        var built = await BuildAsync(ids, ct);
        var items = ids.Where(built.Tickets.ContainsKey).Select(id => built.Tickets[id]).ToList();
        return Outcome<IntegrationTicketPage>.Ok(new IntegrationTicketPage(items, page, pageSize, total));
    }

    /// <summary>The account with this e-mail address, however it is written (upper or lower case). The address is stored encrypted: it is found by its lookup hash.</summary>
    private async Task<Guid?> FindUserIdAsync(string email, CancellationToken ct)
    {
        var lookup = _users.NormalizeEmail(email.Trim());
        return await _users.Users.AsNoTracking().Where(u => u.NormalizedEmail == lookup).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    private sealed record Built(Dictionary<Guid, IntegrationTicket> Tickets, Dictionary<Guid, IntegrationPerson> People);

    /// <summary>The tickets with everything around them, in a few queries for the whole page and not a few per ticket.</summary>
    private async Task<Built> BuildAsync(List<Guid> ticketIds, CancellationToken ct)
    {
        if (ticketIds.Count == 0) return new Built([], []);

        var tickets = await _db.Tickets.AsNoTracking().Where(t => ticketIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.TicketNumber,
                t.Title,
                t.Description,
                t.Status,
                t.Priority,
                t.Source,
                HelpTopic = t.HelpTopic != null ? t.HelpTopic.Name : null,
                Branch = t.Department != null ? t.Department.Name : null,
                SubBranch = t.SubBranch != null ? t.SubBranch.Name : null,
                Company = t.Company != null ? t.Company.Name : null,
                t.ClientId,
                t.CreatedAt,
                t.OpenedAtUtc,
                t.DueDateUtc,
                t.ClosedAtUtc,
                t.ResolutionSummary
            })
            .ToListAsync(ct);
        var assignees = await _db.TicketAssignees.AsNoTracking().Where(a => ticketIds.Contains(a.TicketId))
            .OrderBy(a => a.AssignedAtUtc).ThenBy(a => a.Id)
            .Select(a => new { a.TicketId, a.UserId, a.IsPrimary, a.AssignedAtUtc })
            .ToListAsync(ct);

        var userIds = tickets.Select(t => t.ClientId).Concat(assignees.Select(a => a.UserId)).Distinct().ToList();
        var people = (await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => new IntegrationPerson(u.Email ?? "", $"{u.FirstName} {u.LastName}".Trim()));

        var baseUrl = (_configuration["Frontend:BaseUrl"] ?? "").TrimEnd('/');
        var built = new Dictionary<Guid, IntegrationTicket>();
        foreach (var t in tickets)
        {
            if (!people.TryGetValue(t.ClientId, out var client)) continue;
            var onTicket = assignees.Where(a => a.TicketId == t.Id && people.ContainsKey(a.UserId))
                .Select(a => new IntegrationAssignee(people[a.UserId].Email, people[a.UserId].Name, a.IsPrimary, a.AssignedAtUtc))
                .ToList();
            built[t.Id] = new IntegrationTicket(
                t.Id, t.TicketNumber, t.Title, t.Description, t.Status.ToString(), t.Priority?.ToString(), t.Source?.ToString(),
                t.HelpTopic, t.Branch, t.SubBranch, t.Company, client,
                t.CreatedAt, t.OpenedAtUtc, t.DueDateUtc, t.ClosedAtUtc, t.ResolutionSummary,
                $"{baseUrl}/tickets/{t.Id}", onTicket);
        }
        return new Built(built, people);
    }

    /// <summary>A time with or without an offset, read as UTC when it says nothing (a query string would otherwise be moved to the server's own zone).</summary>
    private static bool TryParseUtc(string? text, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) return false;
        utc = DateTime.SpecifyKind(parsed.UtcDateTime, DateTimeKind.Utc);
        return true;
    }
}
