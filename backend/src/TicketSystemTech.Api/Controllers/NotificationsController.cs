using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Api.Services;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of a person's own notifications.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int take = 20) =>
        Ok(await _notifications.ListAsync(take, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id) =>
        this.ToResult(await _notifications.MarkReadAsync(id));

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead() =>
        this.ToResult(await _notifications.MarkAllReadAsync());
}
