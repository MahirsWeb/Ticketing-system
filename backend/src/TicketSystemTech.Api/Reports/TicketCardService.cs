using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Integration;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Api.Tickets;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Reports;

/// <summary>
/// The card of a ticket: its whole life in one line of time, for staff who may open the ticket (the rule of <see cref="TicketAccess"/>). A client does not get it: it holds
/// who did what, branches, transfers and times that are not theirs to see; what a client is given is the "for the client" monthly report. Read only.
/// </summary>
public interface ITicketCardService
{
    Task<Outcome<TicketCardDto>> CardAsync(Guid ticketId, CancellationToken ct);

    /// <summary>The same card as an xlsx workbook, in the language asked for or, without one, the language of the person who asks.</summary>
    Task<Outcome<ExportFile>> ExportAsync(Guid ticketId, string? language, CancellationToken ct);
}

public sealed class TicketCardService : ITicketCardService
{
    private static readonly TimeSpan SameMoment = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditTrailReader _audit;
    private readonly TimeProvider _time;
    private readonly Lazy<TimeZoneInfo> _zone;

    public TicketCardService(AppDbContext db, ICurrentUserService currentUser, IAuditTrailReader audit, IOptions<SlaOptions> sla, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _time = time;
        _zone = new Lazy<TimeZoneInfo>(() => BusinessTimeZone.Resolve(sla.Value.TimeZoneId));
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<Outcome<TicketCardDto>> CardAsync(Guid ticketId, CancellationToken ct)
    {
        var ticket = await _db.Tickets.AsNoTracking().Include(t => t.Company).Include(t => t.Department).FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null) return Outcome<TicketCardDto>.NotFound();
        if (_currentUser.Role == UserRole.Client || !TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketCardDto>.Forbidden();
        var now = UtcNow;

        var trail = await _audit.ForTicketAsync(ticketId, ct);
        var assignees = await _db.TicketAssignees.AsNoTracking().Where(a => a.TicketId == ticketId).Select(a => new { a.UserId, a.IsPrimary, a.AssignedAtUtc }).ToListAsync(ct);
        var messages = await _db.TicketMessages.AsNoTracking().Where(m => m.TicketId == ticketId && m.Type != MessageType.SystemEvent)
            .Select(m => new { m.AuthorId, m.Type, m.CreatedAt }).ToListAsync(ct);
        var sessions = await _db.TicketWorkSessions.AsNoTracking().Where(s => s.TicketId == ticketId).Select(s => new { s.UserId, s.StartedAtUtc, s.EndedAtUtc }).ToListAsync(ct);

        // The people named anywhere on the card, by id.
        var ids = new HashSet<Guid> { ticket.ClientId };
        foreach (var id in new[] { ticket.OpenedByUserId, ticket.ClosedByUserId }) if (id.HasValue) ids.Add(id.Value);
        foreach (var a in assignees) ids.Add(a.UserId);
        foreach (var m in messages) ids.Add(m.AuthorId);
        foreach (var s in sessions) ids.Add(s.UserId);
        foreach (var row in trail)
        {
            if (row.UserId.HasValue) ids.Add(row.UserId.Value);
            if (TicketAuditDetails.AddedUser(row.Details) is { } added) ids.Add(added);
            if (TicketAuditDetails.RemovedUser(row.Details) is { } removed) ids.Add(removed);
            if (TicketAuditDetails.OpenedAssignee(row.Details) is { } opened) ids.Add(opened);
        }
        var people = (await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, u.Role }).ToListAsync(ct))
            .ToDictionary(u => u.Id);
        string? Name(Guid? id) => id is { } k && people.TryGetValue(k, out var p) ? p.Name : null;
        var departments = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        string? Branch(Guid? id) => id is { } k ? departments.GetValueOrDefault(k) : null;

        var events = new List<TicketCardEventDto>();
        void Add(DateTime at, string kind, string? person = null, string? from = null, string? to = null, long? seconds = null, bool running = false) =>
            events.Add(new TicketCardEventDto(at, kind, person, from, to, seconds, running));

        Add(ticket.CreatedAt, "Created", Name(ticket.ClientId));

        // One source for each kind of event: the audit trail, and where it lacks the fact, the columns of the ticket. The system messages that say the same
        // thing in words (opened, transferred, closed ...) are not used at all.
        var openedRow = trail.FirstOrDefault(r => r.Action == "TicketOpened");
        if (openedRow is not null) Add(openedRow.AtUtc, "Opened", Name(TicketAuditDetails.OpenedAssignee(openedRow.Details)));
        else if (ticket.OpenedAtUtc is { } openedAt) Add(openedAt, "Opened", Name(ticket.OpenedByUserId));

        var assignedUsers = new HashSet<Guid>();
        if (openedRow is not null && TicketAuditDetails.OpenedAssignee(openedRow.Details) is { } first) assignedUsers.Add(first);
        foreach (var row in trail)
        {
            switch (row.Action)
            {
                case "TicketAssigneeAdded" when TicketAuditDetails.AddedUser(row.Details) is { } added:
                    assignedUsers.Add(added);
                    Add(row.AtUtc, "Assigned", Name(added));
                    break;
                case "TicketAssigneeRemoved" when TicketAuditDetails.RemovedUser(row.Details) is { } removed:
                    Add(row.AtUtc, "Unassigned", Name(removed));
                    break;
                case "TicketStatusChanged" when TicketAuditDetails.StatusChange(row.Details) is { } change:
                    Add(row.AtUtc, "StatusChanged", Name(row.UserId), change.From.ToString(), change.To.ToString());
                    break;
                case "TicketTransferred" when TicketAuditDetails.Transfer(row.Details) is { } transfer:
                    Add(row.AtUtc, "Transferred", Name(row.UserId), Branch(transfer.From), Branch(transfer.To) ?? "?");
                    break;
                case "TicketClosed":
                    Add(row.AtUtc, "Closed", Name(row.UserId));
                    break;
                case "TicketReopened":
                    Add(row.AtUtc, "Reopened", Name(row.UserId));
                    break;
            }
        }
        // The people on the ticket now whom no row of the trail puts there.
        foreach (var a in assignees.Where(a => !assignedUsers.Contains(a.UserId)))
            Add(a.AssignedAtUtc, "Assigned", Name(a.UserId));
        if (ticket.Status == TicketStatus.Closed && ticket.ClosedAtUtc is { } closedAt
            && !trail.Any(r => r.Action == "TicketClosed" && (r.AtUtc - closedAt).Duration() <= SameMoment))
            Add(closedAt, "Closed", Name(ticket.ClosedByUserId));

        // Replies and notes: that they happened, when and by whom. Never the text.
        foreach (var m in messages)
        {
            var isClient = people.TryGetValue(m.AuthorId, out var author) && author.Role == UserRole.Client;
            var kind = m.Type == MessageType.InternalNote ? "InternalNote" : isClient ? "ClientReply" : "StaffReply";
            Add(m.CreatedAt, kind, Name(m.AuthorId));
        }
        foreach (var s in sessions)
        {
            var end = s.EndedAtUtc ?? now;
            Add(s.StartedAtUtc, "WorkSession", Name(s.UserId), seconds: end > s.StartedAtUtc ? (long)(end - s.StartedAtUtc).TotalSeconds : 0, running: s.EndedAtUtc is null);
        }

        // The time in each status, as far as the trail says.
        var timeline = TicketStatusTimelineBuilder.Build(ticket.CreatedAt, ticket.OpenedAtUtc, ticket.ClosedAtUtc, ticket.Status,
            trail.Select(r => new TrailEvent(r.AtUtc, r.Action, r.Details)));
        long Seconds(StatusSpan s) => ((s.ToUtc ?? now) is var end && end > s.FromUtc) ? (long)(end - s.FromUtc).TotalSeconds : 0;
        var spans = timeline.Spans.Where(s => Seconds(s) > 0 || s.ToUtc is null).Select(s => new StatusSpanDto(s.Status?.ToString(), s.FromUtc, s.ToUtc, Seconds(s))).ToList();
        var totals = timeline.Totals(now).OrderBy(t => t.Key).Select(t => new StatusTotalDto(t.Key.ToString(), (long)t.Value.TotalSeconds)).ToList();
        var unknown = (long)timeline.Unknown(now).TotalSeconds;

        var ordered = events.OrderBy(e => e.AtUtc).ToList();
        var workSeconds = sessions.Sum(s => WorkTimeSummary.Seconds(new WorkEntry(Guid.Empty, s.UserId, false, ticketId, "", "", null, null, s.StartedAtUtc, s.EndedAtUtc), now));
        double? resolution = ticket.OpenedAtUtc.HasValue && ticket.ClosedAtUtc.HasValue ? Math.Round((ticket.ClosedAtUtc.Value - ticket.OpenedAtUtc.Value).TotalHours, 1) : null;
        bool? sla = ticket.Status == TicketStatus.Closed && ticket.ClosedAtUtc.HasValue && ticket.DueDateUtc.HasValue ? ticket.ClosedAtUtc <= ticket.DueDateUtc : null;
        var primary = assignees.FirstOrDefault(a => a.IsPrimary);

        var header = new TicketCardHeaderDto(ticket.Id, ticket.TicketNumber, ticket.Title, ticket.Status, ticket.Company?.Name ?? "", Name(ticket.ClientId) ?? "",
            ticket.Department?.Name, Name(primary?.UserId), ticket.Priority?.ToString(), ticket.CreatedAt, ticket.OpenedAtUtc, ticket.ClosedAtUtc,
            resolution, ticket.DueDateUtc, sla,
            ordered.Count(e => e.Kind == "StaffReply"), ordered.Count(e => e.Kind == "ClientReply"),
            ordered.Count(e => e.Kind == "InternalNote"), WorkTimeSummary.Hours(workSeconds));

        return Outcome<TicketCardDto>.Ok(new TicketCardDto(now, header, ordered, spans, totals, unknown, unknown == 0));
    }

    // ---------------------------------------------------------------- the workbook

    public async Task<Outcome<ExportFile>> ExportAsync(Guid ticketId, string? language, CancellationToken ct)
    {
        var outcome = await CardAsync(ticketId, ct);
        if (!outcome.Succeeded) return outcome.Failure == Failure.Forbidden ? Outcome<ExportFile>.Forbidden() : Outcome<ExportFile>.NotFound();
        var card = outcome.Value!;
        var makerId = _currentUser.UserId;
        var maker = await _db.Users.AsNoTracking().Where(u => u.Id == makerId).Select(u => new { Name = u.FirstName + " " + u.LastName, u.PreferredLanguage }).FirstAsync(ct);
        var t = ReportText.For(language is null ? maker.PreferredLanguage : EmailLanguage.Normalize(language));
        var zone = _zone.Value;

        string Local(DateTime? utc) => utc is { } v ? TimeZoneInfo.ConvertTimeFromUtc(BusinessTimeZone.AsUtc(v), zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "";
        string Status(string? status) => status is null ? t("tc_unknown") : t("st_" + status);
        var h = card.Header;

        var header = new List<(string, string)>
        {
            (t("tc_title"), h.TicketNumber),
            (t("ts_madeBy"), maker.Name),
            (t("ts_madeAt"), Local(card.AsOfUtc) + " (" + zone.Id + ")"),
            (t("ts_snapshotLabel"), t("doc_snapshot")),
        };

        string YesNo(bool? v) => v is null ? "" : v.Value ? t("tc_yes") : t("tc_no");
        var facts = new List<object?[]>
        {
            new object?[] { t("tc_ticket"), $"{h.TicketNumber} {h.Title}" },
            new object?[] { t("tc_status"), Status(h.Status.ToString()) },
            new object?[] { t("tc_company"), h.Company },
            new object?[] { t("tc_client"), h.Client },
        };
        facts.Add(new object?[] { t("tc_branch"), h.Branch ?? "" });
        facts.Add(new object?[] { t("tc_assignee"), h.Assignee ?? "" });
        facts.Add(new object?[] { t("tc_priority"), h.Priority ?? "" });
        facts.Add(new object?[] { t("tc_created"), Local(h.CreatedAt) });
        facts.Add(new object?[] { t("tc_opened"), Local(h.OpenedAtUtc) });
        facts.Add(new object?[] { t("tc_closed"), Local(h.ClosedAtUtc) });
        facts.Add(new object?[] { t("tc_resolution"), h.ResolutionHours });
        facts.Add(new object?[] { t("tc_due"), Local(h.DueDateUtc) });
        facts.Add(new object?[] { t("tc_sla"), YesNo(h.SlaMet) });
        facts.Add(new object?[] { t("tc_staffReplies"), h.StaffReplies });
        facts.Add(new object?[] { t("tc_clientReplies"), h.ClientReplies });
        facts.Add(new object?[] { t("tc_internalNotes"), h.InternalNotes });
        facts.Add(new object?[] { t("tc_workHours"), h.WorkHours });

        string Detail(TicketCardEventDto e) => e.Kind switch
        {
            "StatusChanged" => $"{Status(e.From)} → {Status(e.To)}",
            "Transferred" => $"{e.From ?? t("tc_unrouted")} → {e.To}",
            "WorkSession" => $"{WorkTimeSummary.Hours(e.DurationSeconds ?? 0).ToString("0.00", CultureInfo.InvariantCulture)} h" + (e.Running ? " (" + t("ts_running") + ")" : ""),
            _ => ""
        };
        var timeline = card.Events.Select(e => new object?[] { Local(e.AtUtc), t("ev_" + e.Kind), e.Person ?? "", Detail(e) }).ToList();

        var spans = card.Spans.Select(s => new object?[] { Status(s.Status), Local(s.FromUtc), s.ToUtc is null ? t("tc_stillGoing") : Local(s.ToUtc), WorkTimeSummary.Hours(s.Seconds) }).ToList();
        var totals = card.StatusTotals.Select(s => new object?[] { Status(s.Status), WorkTimeSummary.Hours(s.Seconds) }).ToList();
        if (card.UnknownSeconds > 0) totals.Add(new object?[] { t("tc_unknown"), WorkTimeSummary.Hours(card.UnknownSeconds) });

        var sheets = new List<DocSheet>
        {
            new(t("tc_sheetCard"), header, new List<DocTable> { new(new[] { t("tc_field"), t("tc_value") }, facts) }),
            new(t("tc_sheetTimeline"), header, new List<DocTable> { new(new[] { t("tc_time"), t("tc_event"), t("tc_person"), t("tc_details") }, timeline) }),
            new(t("tc_sheetStatus"), header, new List<DocTable>
            {
                new(new[] { t("tc_status"), t("tc_from"), t("tc_to"), t("tc_hours") }, spans),
                new(new[] { t("tc_status"), t("tc_hours") }, totals),
            }),
        };
        if (!card.TrailComplete) sheets[2].Header.Add((t("tc_unknown"), t("tc_unknownNote")));

        return Outcome<ExportFile>.Ok(new ExportFile(DocumentWorkbook.Build(sheets), $"ticket-card-{AsciiNumber(h.TicketNumber)}.xlsx"));
    }

    private static string AsciiNumber(string number)
    {
        var cleaned = new string(number.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
        return cleaned.Length == 0 ? "ticket" : cleaned.ToLowerInvariant();
    }
}
