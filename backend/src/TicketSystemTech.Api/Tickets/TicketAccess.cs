using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Tickets;

/// <summary>Who may reach a ticket. One place, so a list, a detail page and every action agree.</summary>
public static class TicketAccess
{
    public static bool CanAccess(Ticket ticket, ICurrentUserService user) => user.Role switch
    {
        UserRole.Admin => true,
        // Branches are isolated, strictly: an employee reaches only their own branch's tickets.
        UserRole.Employee => ticket.DepartmentId != null && ticket.DepartmentId == user.DepartmentId,
        UserRole.Client => ticket.ClientId == user.UserId,
        _ => false
    };
}
