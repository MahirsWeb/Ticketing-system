using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 20)
    {
        var items = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == _currentUser.UserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(n => new { n.Id, n.Type, n.Message, n.TicketId, n.IsRead, n.CreatedAt })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == _currentUser.UserId);
        if (notification is null) return NotFound();
        notification.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var items = await _db.Notifications.Where(n => n.UserId == _currentUser.UserId && !n.IsRead).ToListAsync();
        items.ForEach(n => n.IsRead = true);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
