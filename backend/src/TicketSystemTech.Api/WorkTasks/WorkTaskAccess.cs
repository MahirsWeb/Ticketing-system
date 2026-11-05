using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.WorkTasks;

/// <summary>Who may reach a task: Admin everywhere, an employee in their own branch.</summary>
public static class WorkTaskAccess
{
    public static bool CanAccess(WorkTask task, ICurrentUserService user) =>
        user.Role == UserRole.Admin || task.DepartmentId == user.DepartmentId;
}
