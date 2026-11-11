using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.WorkTime;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using static TicketSystemTech.Api.Tickets.TicketHelpers;

namespace TicketSystemTech.Api.Tickets;

// A ticket can have any number of sessions (the morning and the afternoon are two). They are the work of the person the
// ticket is assigned to; people added as extra help are listed on the ticket but log no time of their own.
public interface ITicketWorkSessionService
{
    /// <summary>Logs a stretch of work on the ticket, for the person it is assigned to.</summary>
    Task<Outcome<TicketDetailDto>> AddAsync(Guid id, WorkSessionRequest request);

    /// <summary>Changes the times of a session. It stays with the person who did the work, whoever the ticket belongs to now.</summary>
    Task<Outcome<TicketDetailDto>> UpdateAsync(Guid id, Guid sessionId, WorkSessionRequest request);

    Task<Outcome<TicketDetailDto>> DeleteAsync(Guid id, Guid sessionId);
}

public sealed class TicketWorkSessionService : ITicketWorkSessionService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITicketQueries _queries;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public TicketWorkSessionService(AppDbContext db, ICurrentUserService currentUser, ITicketQueries queries, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _queries = queries;
        _time = time;
    }

    public async Task<Outcome<TicketDetailDto>> AddAsync(Guid id, WorkSessionRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();
        var primaryId = await _queries.PrimaryAssigneeIdAsync(id);
        if (primaryId is null)
            return Outcome<TicketDetailDto>.Invalid("Assign the ticket to somebody before logging work time on it.");

        var started = AsUtc(request.StartedAtUtc!.Value);
        var ended = request.EndedAtUtc.HasValue ? AsUtc(request.EndedAtUtc.Value) : (DateTime?)null;
        var error = WorkSessionRules.Validate(started, ended, UtcNow, await OtherSessionsAsync(id, null))
            ?? WorkSessionRules.ValidateAgainstOtherWork(started, ended, await _db.OtherWorkOfAsync(primaryId.Value, id, null));
        if (error is not null) return Outcome<TicketDetailDto>.Invalid(error);

        var session = new TicketWorkSession { TicketId = id, UserId = primaryId.Value, StartedAtUtc = started, EndedAtUtc = ended };
        _db.TicketWorkSessions.Add(session);
        AuditWorkSession("WorkSessionAdded", id, session);
        await _db.SaveChangesAsync();

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> UpdateAsync(Guid id, Guid sessionId, WorkSessionRequest request)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();

        var session = await _db.TicketWorkSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.TicketId == id);
        if (session is null) return Outcome<TicketDetailDto>.NotFound();
        if (!await MayChangeSessionAsync(session)) return Outcome<TicketDetailDto>.Forbidden();

        var started = AsUtc(request.StartedAtUtc!.Value);
        var ended = request.EndedAtUtc.HasValue ? AsUtc(request.EndedAtUtc.Value) : (DateTime?)null;
        var error = WorkSessionRules.Validate(started, ended, UtcNow, await OtherSessionsAsync(id, sessionId))
            ?? WorkSessionRules.ValidateAgainstOtherWork(started, ended, await _db.OtherWorkOfAsync(session.UserId, id, null));
        if (error is not null) return Outcome<TicketDetailDto>.Invalid(error);

        var (wasStarted, wasEnded) = (session.StartedAtUtc, session.EndedAtUtc);
        session.StartedAtUtc = started;
        session.EndedAtUtc = ended;
        AuditWorkSession("WorkSessionChanged", id, session, (wasStarted, wasEnded));
        await _db.SaveChangesAsync();

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    public async Task<Outcome<TicketDetailDto>> DeleteAsync(Guid id, Guid sessionId)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return Outcome<TicketDetailDto>.NotFound();
        if (!TicketAccess.CanAccess(ticket, _currentUser)) return Outcome<TicketDetailDto>.Forbidden();

        var session = await _db.TicketWorkSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.TicketId == id);
        if (session is null) return Outcome<TicketDetailDto>.NotFound();
        if (!await MayChangeSessionAsync(session)) return Outcome<TicketDetailDto>.Forbidden();

        _db.TicketWorkSessions.Remove(session);
        AuditWorkSession("WorkSessionDeleted", id, session);
        await _db.SaveChangesAsync();

        return Outcome<TicketDetailDto>.Ok(await _queries.BuildDetailAsync(id));
    }

    /// <summary>The time somebody logged is theirs: they may change it, an Admin may, and so may the person the ticket is assigned to now
    /// (who answers for it). A colleague who merely has access to the ticket may not.</summary>
    private async Task<bool> MayChangeSessionAsync(TicketWorkSession session)
    {
        if (_currentUser.Role == UserRole.Admin || session.UserId == _currentUser.UserId) return true;
        return _currentUser.UserId.HasValue && await _queries.PrimaryAssigneeIdAsync(session.TicketId) == _currentUser.UserId;
    }

    private async Task<List<WorkSessionRules.Span>> OtherSessionsAsync(Guid ticketId, Guid? exceptSessionId) =>
        await _db.TicketWorkSessions.AsNoTracking()
            .Where(s => s.TicketId == ticketId && s.Id != exceptSessionId)
            .Select(s => new WorkSessionRules.Span(s.Id, s.StartedAtUtc, s.EndedAtUtc))
            .ToListAsync();

    /// <summary>A change records what the session was as well as what it is now; an addition or a deletion records the session itself.</summary>
    private void AuditWorkSession(string action, Guid ticketId, TicketWorkSession session, (DateTime Started, DateTime? Ended)? was = null) =>
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Action = action,
            EntityType = nameof(Ticket),
            EntityId = ticketId,
            Details = was is { } before
                ? WorkSessionAuditText.Changed(session.Id, session.UserId, before.Started, before.Ended, session.StartedAtUtc, session.EndedAtUtc)
                : WorkSessionAuditText.Of(session.Id, session.UserId, session.StartedAtUtc, session.EndedAtUtc)
        });
}
