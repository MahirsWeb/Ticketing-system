using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            var value = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public UserRole? Role
    {
        get
        {
            var value = User?.FindFirstValue(ClaimTypes.Role);
            return Enum.TryParse<UserRole>(value, out var role) ? role : null;
        }
    }

    public Guid? CompanyId
    {
        get
        {
            var value = User?.FindFirstValue("companyId");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid? DepartmentId
    {
        get
        {
            var value = User?.FindFirstValue("departmentId");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
