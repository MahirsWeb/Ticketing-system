using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    UserRole? Role { get; }
    Guid? CompanyId { get; }
    Guid? DepartmentId { get; }
    bool IsAuthenticated { get; }
}
