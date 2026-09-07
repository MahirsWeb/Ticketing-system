using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Application.Common.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(Guid userId, string email, string firstName, string lastName, UserRole role, Guid? companyId, Guid? departmentId);
    string GenerateRefreshToken();
}
