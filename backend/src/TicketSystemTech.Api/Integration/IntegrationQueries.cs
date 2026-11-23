using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Integration;

/// <summary>
/// What another application reads. Five questions: "who was put on a ticket since I last asked" (the feed of assignments, for the application
/// that asks every minute or so), "what happened to tickets since I last asked" (the feed of events: status, closing, somebody taken off, the
/// things the feed of assignments cannot tell), "which tickets is this person on now" (the picture, to start from and to check against) and "what
/// is this one ticket like now" (to see why it left a list), and "who worked how long, when" (the time entries). All of them see every branch: a
/// key is not a person and has no branch.
/// </summary>
public interface IIntegrationQueries
{
    Task<Outcome<IntegrationAssignmentPage>> AssignmentsAsync(string? since, Guid? afterId, string? email, int limit, CancellationToken ct);

    Task<Outcome<IntegrationEventPage>> EventsAsync(string? since, Guid? afterId, int limit, CancellationToken ct);

    Task<Outcome<IntegrationTicketPage>> TicketsAsync(
        string? assigneeEmail, string? state, string? createdFrom, string? createdTo, int page, int pageSize, bool includeWork, CancellationToken ct);

    Task<Outcome<IntegrationTicket>> TicketAsync(Guid id, bool includeWork, CancellationToken ct);

    Task<Outcome<IntegrationTimeEntryPage>> TimeEntriesAsync(string? from, string? to, string? email, int page, int pageSize, CancellationToken ct);
}

public sealed class IntegrationQueries : IIntegrationQueries
{
    public const int MaxAssignmentsPerPage = 500;
    public const int MaxEventsPerPage = 500;
    public const int MaxTicketsPerPage = 100;
    public const int MaxTimeEntriesPerPage = 500;

    /// <summary>The longest period of time entries asked for at once. A longer one is asked for in parts.</summary>
    public const int MaxTimeEntryPeriodDays = 92;

    /// <summary>What the application writes in the audit trail of a ticket, and the name the other application knows it by. A row of the trail with any other action is not an event.</summary>
    private static readonly Dictionary<string, string> EventTypes = new()
    {
        ["TicketOpened"] = "Opened",
        ["TicketAssigneeAdded"] = "AssigneeAdded",
        ["TicketAssigneeRemoved"] = "AssigneeRemoved",
        ["TicketStatusChanged"] = "StatusChanged",
        ["TicketClosed"] = "Closed",
        ["TicketReopened"] = "Reopened",
        ["TicketTransferred"] = "Transferred"
    };

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly Lazy<TimeZoneInfo> _zone;

    public IntegrationQueries(AppDbContext db, UserManager<ApplicationUser> users, IConfiguration configuration, TimeProvider time, IOptions<SlaOptions> sla)
    {
        _db = db;
        _users = users;
        _configuration = configuration;
        _time = time;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(sla.Value.TimeZoneId));
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

        var built = await BuildAsync(rows.Select(r => r.TicketId).Distinct().ToList(), includeWork: false, [], ct);
        var items = rows
            .Where(r => built.Tickets.ContainsKey(r.TicketId) && built.People.ContainsKey(r.UserId))
            .Select(r => new IntegrationAssignment(r.Id, r.AssignedAtUtc, r.IsPrimary, built.People[r.UserId], built.Tickets[r.TicketId]))
            .ToList();

        var last = rows.Count > 0 ? rows[^1] : null;
        return Outcome<IntegrationAssignmentPage>.Ok(new IntegrationAssignmentPage(items, last?.AssignedAtUtc ?? from, last?.Id ?? afterId, hasMore));
    }

    public async Task<Outcome<IntegrationEventPage>> EventsAsync(string? since, Guid? afterId, int limit, CancellationToken ct)
    {
        if (!TryParseUtc(since, out var from))
            return Outcome<IntegrationEventPage>.Invalid("since is required: a UTC time such as 2026-01-01T00:00:00Z.");
        limit = Math.Clamp(limit, 1, MaxEventsPerPage);

        var actions = EventTypes.Keys.ToList();
        var query = _db.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(Ticket) && a.EntityId != null && actions.Contains(a.Action));

        // The same way of going on as in the feed of assignments (see there): the time, and the id to tell apart the rows of one instant.
        query = afterId.HasValue
            ? query.Where(a => a.CreatedAt > from || (a.CreatedAt == from && a.Id.CompareTo(afterId.Value) > 0))
            : query.Where(a => a.CreatedAt >= from);

        var rows = await query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Take(limit + 1)
            .Select(a => new { a.Id, a.CreatedAt, a.Action, TicketId = a.EntityId!.Value, a.Details })
            .ToListAsync(ct);
        var hasMore = rows.Count > limit;
        rows = rows.Take(limit).ToList();

        var read = rows.Select(r => new
        {
            Row = r,
            Person = r.Action switch
            {
                "TicketAssigneeAdded" => TicketAuditDetails.AddedUser(r.Details),
                "TicketAssigneeRemoved" => TicketAuditDetails.RemovedUser(r.Details),
                "TicketOpened" => TicketAuditDetails.OpenedAssignee(r.Details),
                _ => null
            },
            Status = r.Action == "TicketStatusChanged" ? TicketAuditDetails.StatusChange(r.Details) : null
        }).ToList();

        var built = await BuildAsync(rows.Select(r => r.TicketId).Distinct().ToList(), includeWork: false, read.Where(x => x.Person.HasValue).Select(x => x.Person!.Value), ct);
        var items = read.Select(x => new IntegrationEvent(
            x.Row.Id, x.Row.CreatedAt, EventTypes[x.Row.Action], x.Row.TicketId,
            x.Person is { } p && built.People.TryGetValue(p, out var person) ? person : null,
            x.Status?.From.ToString(), x.Status?.To.ToString(),
            built.Tickets.GetValueOrDefault(x.Row.TicketId))).ToList();

        var last = rows.Count > 0 ? rows[^1] : null;
        return Outcome<IntegrationEventPage>.Ok(new IntegrationEventPage(items, last?.CreatedAt ?? from, last?.Id ?? afterId, hasMore));
    }

    public async Task<Outcome<IntegrationTicket>> TicketAsync(Guid id, bool includeWork, CancellationToken ct)
    {
        var built = await BuildAsync([id], includeWork, [], ct);
        return built.Tickets.TryGetValue(id, out var ticket)
            ? Outcome<IntegrationTicket>.Ok(ticket)
            : Outcome<IntegrationTicket>.NotFound("There is no ticket with this id.");
    }

    public async Task<Outcome<IntegrationTicketPage>> TicketsAsync(
        string? assigneeEmail, string? state, string? createdFrom, string? createdTo, int page, int pageSize, bool includeWork, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxTicketsPerPage);
        var wanted = (state ?? "active").Trim().ToLowerInvariant();
        if (wanted is not ("active" or "closed" or "all"))
            return Outcome<IntegrationTicketPage>.Invalid("state must be active (being worked on), closed or all.");

        DateTime? from = null, to = null;
        if (!string.IsNullOrWhiteSpace(createdFrom))
        {
            if (!TryParseUtc(createdFrom, out var parsed)) return Outcome<IntegrationTicketPage>.Invalid("createdFrom must be a UTC time or a date such as 2026-11-01.");
            from = parsed;
        }
        if (!string.IsNullOrWhiteSpace(createdTo))
        {
            if (!TryParseUtc(createdTo, out var parsed)) return Outcome<IntegrationTicketPage>.Invalid("createdTo must be a UTC time or a date such as 2026-12-01.");
            to = parsed;
        }
        if (from is { } a && to is { } b && b <= a) return Outcome<IntegrationTicketPage>.Invalid("createdTo must be later than createdFrom.");

        var query = _db.Tickets.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(t => t.CreatedAt >= from.Value); // from is included, to is not: the next period starts where this one ends
        if (to.HasValue) query = query.Where(t => t.CreatedAt < to.Value);
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

        var built = await BuildAsync(ids, includeWork, [], ct);
        var items = ids.Where(built.Tickets.ContainsKey).Select(id => built.Tickets[id]).ToList();
        return Outcome<IntegrationTicketPage>.Ok(new IntegrationTicketPage(items, page, pageSize, total));
    }

    private sealed class TimeRow
    {
        public string Kind { get; set; } = "";
        public Guid Id { get; set; }
        public Guid? TicketId { get; set; }
        public string? TicketNumber { get; set; }
        public Guid? TaskId { get; set; }
        public string? TaskTitle { get; set; }
        public Guid UserId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
    }

    public async Task<Outcome<IntegrationTimeEntryPage>> TimeEntriesAsync(string? from, string? to, string? email, int page, int pageSize, CancellationToken ct)
    {
        if (!TryParseDate(from, out var first))
            return Outcome<IntegrationTimeEntryPage>.Invalid("from is required: a date such as 2026-11-01, the day the work started on (in the time zone of the business).");
        var zone = _zone.Value;
        var now = _time.GetUtcNow().UtcDateTime;
        DateOnly last;
        if (string.IsNullOrWhiteSpace(to)) last = new[] { first, BusinessTimeZone.LocalDate(now, zone) }.Max(); // up to today
        else if (!TryParseDate(to, out last)) return Outcome<IntegrationTimeEntryPage>.Invalid("to must be a date such as 2026-11-30.");
        if (last < first) return Outcome<IntegrationTimeEntryPage>.Invalid("to must not be before from.");
        if (last.DayNumber - first.DayNumber >= MaxTimeEntryPeriodDays)
            return Outcome<IntegrationTimeEntryPage>.Invalid($"The period is at most {MaxTimeEntryPeriodDays} days. Ask for a longer one in parts.");
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxTimeEntriesPerPage);

        Guid? personId = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            personId = await FindUserIdAsync(email, ct);
            if (personId is null) return Outcome<IntegrationTimeEntryPage>.Ok(new IntegrationTimeEntryPage([], page, pageSize, 0));
        }

        // A day is the local calendar day of the business, so the period runs from the start of the first day to the end of the last, in UTC.
        var startUtc = BusinessTimeZone.DayWindow(first.ToDateTime(TimeOnly.MinValue), zone, now).StartUtc;
        var endUtc = BusinessTimeZone.DayWindow(last.ToDateTime(TimeOnly.MinValue), zone, now).EndUtc;

        var onTickets = _db.TicketWorkSessions.AsNoTracking()
            .Where(s => s.StartedAtUtc >= startUtc && s.StartedAtUtc < endUtc && (personId == null || s.UserId == personId))
            .Select(s => new TimeRow
            {
                Kind = "Ticket", Id = s.Id, TicketId = s.TicketId, TicketNumber = s.Ticket!.TicketNumber, TaskId = null, TaskTitle = null,
                UserId = s.UserId, StartedAtUtc = s.StartedAtUtc, EndedAtUtc = s.EndedAtUtc
            });
        var onTasks = _db.WorkTaskSessions.AsNoTracking()
            .Where(s => s.StartedAtUtc >= startUtc && s.StartedAtUtc < endUtc && (personId == null || s.UserId == personId))
            .Select(s => new TimeRow
            {
                Kind = "Task", Id = s.Id, TicketId = null, TicketNumber = null, TaskId = s.WorkTaskId, TaskTitle = s.WorkTask!.Title,
                UserId = s.UserId, StartedAtUtc = s.StartedAtUtc, EndedAtUtc = s.EndedAtUtc
            });
        var all = onTickets.Concat(onTasks);

        var total = await all.CountAsync(ct);
        var rows = await all.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var people = (await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => new IntegrationPerson(u.Email ?? "", $"{u.FirstName} {u.LastName}".Trim()));

        var items = rows.Where(r => people.ContainsKey(r.UserId)).Select(r =>
        {
            var seconds = r.EndedAtUtc is { } ended ? (long)Math.Max(0, (ended - r.StartedAtUtc).TotalSeconds) : (long?)null;
            var minutes = r.EndedAtUtc is { } end ? (int)Math.Round(Math.Max(0, (end - r.StartedAtUtc).TotalMinutes), MidpointRounding.AwayFromZero) : (int?)null;
            return new IntegrationTimeEntry(
                r.Id, r.Kind, r.TicketId, r.TicketNumber, r.TaskId, r.TaskTitle, people[r.UserId],
                BusinessTimeZone.LocalDate(r.StartedAtUtc, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.StartedAtUtc, r.EndedAtUtc, r.EndedAtUtc is null, seconds, minutes);
        }).ToList();

        return Outcome<IntegrationTimeEntryPage>.Ok(new IntegrationTimeEntryPage(items, page, pageSize, total));
    }

    /// <summary>The account with this e-mail address, however it is written (upper or lower case). The address is stored encrypted: it is found by its lookup hash.</summary>
    private async Task<Guid?> FindUserIdAsync(string email, CancellationToken ct)
    {
        var lookup = _users.NormalizeEmail(email.Trim());
        return await _users.Users.AsNoTracking().Where(u => u.NormalizedEmail == lookup).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
    }

    private sealed record Built(Dictionary<Guid, IntegrationTicket> Tickets, Dictionary<Guid, IntegrationPerson> People);

    private sealed record SessionRow(Guid Id, Guid TicketId, Guid UserId, DateTime StartedAtUtc, DateTime? EndedAtUtc);

    /// <summary>
    /// The tickets with everything around them, in a few queries for the whole page and not a few per ticket. <paramref name="includeWork"/> adds
    /// how long people worked on each; <paramref name="alsoPeople"/> are people the caller needs besides those on the tickets (somebody taken off a ticket is no longer on it).
    /// </summary>
    private async Task<Built> BuildAsync(List<Guid> ticketIds, bool includeWork, IEnumerable<Guid> alsoPeople, CancellationToken ct)
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

        var sessions = includeWork
            ? await _db.TicketWorkSessions.AsNoTracking().Where(s => ticketIds.Contains(s.TicketId))
                .OrderBy(s => s.StartedAtUtc).ThenBy(s => s.Id)
                .Select(s => new SessionRow(s.Id, s.TicketId, s.UserId, s.StartedAtUtc, s.EndedAtUtc))
                .ToListAsync(ct)
            : [];

        var userIds = tickets.Select(t => t.ClientId).Concat(assignees.Select(a => a.UserId)).Concat(sessions.Select(s => s.UserId)).Concat(alsoPeople).Distinct().ToList();
        var people = (await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.FirstName, u.LastName }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => new IntegrationPerson(u.Email ?? "", $"{u.FirstName} {u.LastName}".Trim()));

        var baseUrl = (_configuration["Frontend:BaseUrl"] ?? "").TrimEnd('/');
        var now = _time.GetUtcNow().UtcDateTime;
        var built = new Dictionary<Guid, IntegrationTicket>();
        foreach (var t in tickets)
        {
            if (!people.TryGetValue(t.ClientId, out var client)) continue;
            var onTicket = assignees.Where(a => a.TicketId == t.Id && people.ContainsKey(a.UserId))
                .Select(a => new IntegrationAssignee(people[a.UserId].Email, people[a.UserId].Name, a.IsPrimary, a.AssignedAtUtc))
                .ToList();

            IReadOnlyList<IntegrationWorkSession>? work = null;
            long? workedSeconds = null;
            if (includeWork)
            {
                var logged = sessions.Where(s => s.TicketId == t.Id && people.ContainsKey(s.UserId)).ToList();
                work = logged.Select(s => new IntegrationWorkSession(s.Id, people[s.UserId], s.StartedAtUtc, s.EndedAtUtc)).ToList();
                workedSeconds = (long)logged.Sum(s => Math.Max(0, ((s.EndedAtUtc ?? now) - s.StartedAtUtc).TotalSeconds));
            }

            built[t.Id] = new IntegrationTicket(
                t.Id, t.TicketNumber, t.Title, t.Description, t.Status.ToString(), t.Priority?.ToString(), t.Source?.ToString(),
                t.HelpTopic, t.Branch, t.SubBranch, t.Company, client,
                t.CreatedAt, t.OpenedAtUtc, t.DueDateUtc, t.ClosedAtUtc, t.ResolutionSummary,
                $"{baseUrl}/tickets/{t.Id}", onTicket, work, workedSeconds);
        }
        return new Built(built, people);
    }

    /// <summary>A day, written year-month-day, as the other application would write it.</summary>
    private static bool TryParseDate(string? text, out DateOnly day)
    {
        day = default;
        return !string.IsNullOrWhiteSpace(text) && DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
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
