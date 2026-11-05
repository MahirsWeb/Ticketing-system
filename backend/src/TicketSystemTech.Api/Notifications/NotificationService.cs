using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Notifications;

/// <summary>A person's own notifications: nobody reads or marks anybody else's.</summary>
public interface INotificationService
{
    /// <summary>The newest notifications of the current user (at most 100).</summary>
    Task<object> ListAsync(int take, CancellationToken ct);

    Task<Outcome<Done>> MarkReadAsync(Guid id);

    Task<Outcome<Done>> MarkAllReadAsync();
}

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public NotificationService(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<object> ListAsync(int take, CancellationToken ct) =>
        await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == _currentUser.UserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(n => new { n.Id, n.Type, n.Message, n.TicketId, n.IsRead, n.CreatedAt })
            .ToListAsync(ct);

    public async Task<Outcome<Done>> MarkReadAsync(Guid id)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == _currentUser.UserId);
        if (notification is null) return Outcome<Done>.NotFound();
        notification.IsRead = true;
        await _db.SaveChangesAsync();
        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<Outcome<Done>> MarkAllReadAsync()
    {
        var items = await _db.Notifications.Where(n => n.UserId == _currentUser.UserId && !n.IsRead).ToListAsync();
        items.ForEach(n => n.IsRead = true);
        await _db.SaveChangesAsync();
        return Outcome<Done>.Ok(Done.Value);
    }
}
