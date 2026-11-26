using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Reports;

// The extra statistics of the companies. Same rules as the rest: totals and names only, never a ticket, a title or a text; the group of tickets each one counts is
// said in its contract (CompanyReportContracts.cs) and on the screen. No new column or index is needed: everything is read from what the tickets, the messages and the
// work sessions already hold.
public sealed partial class CompanyReportService
{
    /// <summary>The longest list of repeated reports that is given.</summary>
    public const int MostRepeated = 50;

    // ---------------------------------------------------------------- open now, by age

    public async Task<Outcome<OpenAgeDto>> OpenAgeAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<OpenAgeDto>(check);

        var (from, to) = Range(filter);
        var open = (await ScopeAsync(filter, from, to, ct)).Where(t => t.Status != TicketStatus.Closed);
        var now = UtcNow;
        var d2 = now.AddDays(-2);
        var d7 = now.AddDays(-7);
        var d30 = now.AddDays(-30);

        var counts = await open.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            A = g.Count(t => t.CreatedAt >= d2),
            B = g.Count(t => t.CreatedAt < d2 && t.CreatedAt >= d7),
            C = g.Count(t => t.CreatedAt < d7 && t.CreatedAt >= d30),
        }).FirstOrDefaultAsync(ct);
        if (counts is null || counts.Total == 0) return Outcome<OpenAgeDto>.Ok(new OpenAgeDto(0, 0, 0, 0, 0, 0, null));

        var oldest = await open.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => new { t.CreatedAt, Company = t.Company!.Name }).FirstAsync(ct);
        var days = (int)Math.Max(0, Math.Floor((now - oldest.CreatedAt).TotalDays));
        return Outcome<OpenAgeDto>.Ok(new OpenAgeDto(counts.Total, counts.A, counts.B, counts.C, counts.Total - counts.A - counts.B - counts.C, days, oldest.Company));
    }

    // ---------------------------------------------------------------- time written down

    public async Task<Outcome<TimeSpentDto>> TimeSpentAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<TimeSpentDto>(check);

        var (from, to) = Range(filter);
        var scope = await ScopeAsync(filter, from, to, ct);
        var sessions = _db.TicketWorkSessions.AsNoTracking()
            .Join(scope, s => s.TicketId, t => t.Id, (s, t) => s)
            .Where(s => s.StartedAtUtc >= from && s.StartedAtUtc <= to);

        var ended = sessions.Where(s => s.EndedAtUtc != null);
        var count = await ended.CountAsync(ct);
        var running = await sessions.CountAsync(s => s.EndedAtUtc == null, ct);
        if (count == 0) return Outcome<TimeSpentDto>.Ok(new TimeSpentDto(0, 0, 0, 0, running));

        var seconds = await ended.SumAsync(s => (long)EF.Functions.DateDiffSecond(s.StartedAtUtc, s.EndedAtUtc!.Value), ct);
        var tickets = await ended.Select(s => s.TicketId).Distinct().CountAsync(ct);
        var hours = Math.Round(seconds / 3600.0, 1);
        return Outcome<TimeSpentDto>.Ok(new TimeSpentDto(hours, count, tickets, Math.Round(seconds / 3600.0 / tickets, 1), running));
    }

    // ---------------------------------------------------------------- conversation and pick-up

    public async Task<Outcome<ConversationDto>> ConversationAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<ConversationDto>(check);

        var (from, to) = Range(filter);
        var tickets = CreatedIn(await ScopeAsync(filter, from, to, ct), from, to);

        var total = await tickets.CountAsync(ct);
        if (total == 0) return Outcome<ConversationDto>.Ok(new ConversationDto(0, 0, 0, 0, 0));

        var messages = await _db.TicketMessages.AsNoTracking()
            .Join(tickets, m => m.TicketId, t => t.Id, (m, t) => m)
            .CountAsync(m => m.Type == MessageType.Response, ct);
        var pickedUp = await tickets.CountAsync(t => t.OpenedAtUtc != null, ct);
        var minutes = await tickets.Where(t => t.OpenedAtUtc != null).AverageAsync(t => (double?)EF.Functions.DateDiffMinute(t.CreatedAt, t.OpenedAtUtc), ct);

        return Outcome<ConversationDto>.Ok(new ConversationDto(total, Math.Round((double)messages / total, 1), pickedUp, Hours(minutes), total - pickedUp));
    }

    // ---------------------------------------------------------------- weekday and hour

    public async Task<Outcome<RhythmDto>> RhythmAsync(CompanyReportFilter filter, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<RhythmDto>(check);

        var (from, to) = Range(filter);
        var zone = _zone.Value;
        var created = await CreatedIn(await ScopeAsync(filter, from, to, ct), from, to).Select(t => t.CreatedAt).ToListAsync(ct);

        var weekdays = new int[7];
        var hours = new int[24];
        foreach (var utc in created)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            weekdays[((int)local.DayOfWeek + 6) % 7]++; // Monday first
            hours[local.Hour]++;
        }
        return Outcome<RhythmDto>.Ok(new RhythmDto(created.Count, weekdays.ToList(), hours.ToList(), zone.Id));
    }

    // ---------------------------------------------------------------- the same person, the same topic

    public async Task<Outcome<RepeatedTopicsDto>> RepeatedTopicsAsync(CompanyReportFilter filter, int limit, CancellationToken ct)
    {
        var check = await CheckAsync(filter, companyRequired: false, ct);
        if (!check.Ok) return Refused<RepeatedTopicsDto>(check);

        var (from, to) = Range(filter);
        var groups = await CreatedIn(await ScopeAsync(filter, from, to, ct), from, to)
            .Where(t => t.HelpTopicId != null)
            .GroupBy(t => new { t.ClientId, TopicId = t.HelpTopicId!.Value })
            .Select(g => new { g.Key.ClientId, g.Key.TopicId, Count = g.Count() })
            .Where(g => g.Count >= 2)
            .ToListAsync(ct);

        var top = groups.OrderByDescending(g => g.Count).ThenBy(g => g.ClientId).ThenBy(g => g.TopicId).Take(Math.Clamp(limit, 1, MostRepeated)).ToList();
        var clientIds = top.Select(g => g.ClientId).Distinct().ToList();
        var topicIds = top.Select(g => g.TopicId).Distinct().ToList();
        var clients = await _db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var topics = await _db.HelpTopics.AsNoTracking().Where(h => topicIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        var items = top.Select(g => new RepeatedTopicDto(g.ClientId, clients.GetValueOrDefault(g.ClientId, "Unknown"), g.TopicId, topics.GetValueOrDefault(g.TopicId, "Unknown"), g.Count)).ToList();
        return Outcome<RepeatedTopicsDto>.Ok(new RepeatedTopicsDto(groups.Count, groups.Sum(g => g.Count), items));
    }

    // ---------------------------------------------------------------- companies gone quiet

    public async Task<QuietCompaniesDto> QuietCompaniesAsync(CompanyReportFilter filter, int limit, CancellationToken ct)
    {
        var (from, to) = Range(filter);
        var (prevFrom, prevTo) = Previous(from, to);

        var before = await CountsByCompanyAsync(prevFrom, prevTo, null, ct);
        var now = (await _db.Tickets.AsNoTracking().Where(t => t.CreatedAt >= from && t.CreatedAt <= to).Select(t => t.CompanyId).Distinct().ToListAsync(ct)).ToHashSet();
        var quiet = before.Where(b => !now.Contains(b.Key)).OrderByDescending(b => b.Value).ThenBy(b => b.Key).ToList();

        var shown = quiet.Take(Math.Clamp(limit, 1, MostTop)).ToList();
        var ids = shown.Select(q => q.Key).ToList();
        var names = await _db.Companies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        // The last ticket a company sent, from any time: how long it has been quiet.
        var last = await _db.Tickets.AsNoTracking().Where(t => ids.Contains(t.CompanyId)).GroupBy(t => t.CompanyId)
            .Select(g => new { Id = g.Key, At = g.Max(t => t.CreatedAt) }).ToDictionaryAsync(x => x.Id, x => x.At, ct);

        var items = shown.Select(q => new QuietCompanyDto(q.Key, names.GetValueOrDefault(q.Key, "Unknown"), q.Value, last.TryGetValue(q.Key, out var at) ? at : null)).ToList();
        return new QuietCompaniesDto(quiet.Count, items);
    }
}
