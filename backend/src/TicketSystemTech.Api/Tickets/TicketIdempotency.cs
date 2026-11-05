using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using static TicketSystemTech.Api.Tickets.TicketHelpers;

namespace TicketSystemTech.Api.Tickets;

/// <summary>
/// The same ticket or message sent twice (a double click, a retry after a timeout) must be one: a request that carries an
/// Idempotency-Key the server has already seen gets the first answer back instead of a second copy.
/// </summary>
public interface ITicketIdempotency
{
    Task<Guid?> FindPreviousAsync(Guid userId, string key, string scope = TicketIdempotency.CreateScope);

    /// <summary>
    /// Commits everything staged on the context. When a key was supplied its record goes in the same transaction;
    /// if another request with the same key won the race the unique index rejects this one, nothing is written, and
    /// the winner's resource id is returned so the caller can hand that back instead.
    /// </summary>
    Task<Guid?> SaveAsync(Guid userId, string? key, Guid resourceId, string scope = TicketIdempotency.CreateScope);
}

public sealed class TicketIdempotency : ITicketIdempotency
{
    public const string CreateScope = "ticket-create";
    public const string MessageScope = "ticket-message";

    private readonly AppDbContext _db;

    public TicketIdempotency(AppDbContext db) => _db = db;

    /// <summary>Checks the optional Idempotency-Key header. A present but malformed key is an error, never silently ignored.</summary>
    public static (string? Key, string? Error) Check(string? raw)
    {
        if (raw is null) return (null, null);
        return IdempotencyKey.IsValid(raw)
            ? (raw, null)
            : (null, $"{IdempotencyKey.HeaderName} must be 1-{IdempotencyKey.MaxLength} letters, digits, '-' or '_'.");
    }

    public Task<Guid?> FindPreviousAsync(Guid userId, string key, string scope = CreateScope) =>
        _db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Scope == scope && r.Key == key)
            .Select(r => (Guid?)r.ResourceId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Commits everything staged on the context. When a key was supplied its record goes in the same transaction;
    /// if another request with the same key won the race the unique index rejects this one, nothing is written, and
    /// the winner's ticket id is returned so the caller can hand that back instead.
    /// </summary>
    public async Task<Guid?> SaveAsync(Guid userId, string? key, Guid ticketId, string scope = CreateScope)
    {
        if (key is null)
        {
            await _db.SaveChangesAsync();
            return null;
        }

        _db.IdempotencyRecords.Add(new IdempotencyRecord { UserId = userId, Scope = scope, Key = key, ResourceId = ticketId });
        try
        {
            await _db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var winner = await FindPreviousAsync(userId, key, scope);
            if (winner is null) throw; // not the idempotency index: a genuine failure
            return winner;
        }
    }
}
