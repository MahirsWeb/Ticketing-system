using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TicketSystemTech.Api.Realtime;

[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = Context.User?.FindFirstValue(ClaimTypes.Role);

        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.User(userId));

        if (!string.IsNullOrEmpty(role))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Role(role));

        await base.OnConnectedAsync();
    }
}

public static class GroupNames
{
    public static string User(string userId) => $"user:{userId}";
    public static string Role(string role) => $"role:{role}";
}
