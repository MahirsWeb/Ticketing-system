using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Infrastructure.Services;

public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(Guid userId, string email, string firstName, string lastName, UserRole role, Guid? companyId, Guid? departmentId)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.GivenName, firstName),
            new(ClaimTypes.Surname, lastName),
            new(ClaimTypes.Role, role.ToString())
        };
        if (companyId.HasValue)
            claims.Add(new Claim("companyId", companyId.Value.ToString()));
        if (departmentId.HasValue)
            claims.Add(new Claim("departmentId", departmentId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
