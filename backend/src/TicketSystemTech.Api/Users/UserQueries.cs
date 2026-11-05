using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Users;

/// <summary>Reading accounts. Everything here honours who is asking: an employee sees the staff of their branch and the clients who wrote to it.</summary>
public interface IUserQueries
{
    Task<Outcome<List<UserListItem>>> ListAsync(UserRole? role, Guid? companyId, Guid? departmentId, Guid? subBranchId, CancellationToken ct);

    /// <summary>
    /// The Users screen's list: one page of clients or of staff, optionally filtered by a search text. The plain
    /// <c>GET /api/users</c> loads and decrypts every account on every call, which was fine for the 39 staff the pickers
    /// ask for and wrong for ~2,900 clients.
    ///
    /// Search matches names and the company name as a substring. Email is stored encrypted, so it can only be matched
    /// exactly: type a full address to find that account.
    /// </summary>
    Task<Outcome<UserPageDto>> ListPagedAsync(string group, string? search, int page, int pageSize, CancellationToken ct);

    /// <summary>Fetches a single account's profile — used by the user detail page.</summary>
    Task<Outcome<UserListItem>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Looks up a Client account by email so staff can pre-fill their info when opening a ticket
    /// on behalf of a client who called/reported the issue verbally (not through the client portal).
    /// </summary>
    Task<Outcome<ClientLookupResult>> LookupByEmailAsync(string email, CancellationToken ct);
}

public sealed class UserQueries : IUserQueries
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileLinkService _links;

    public UserQueries(UserManager<ApplicationUser> userManager, AppDbContext db, ICurrentUserService currentUser, IFileLinkService links)
    {
        _userManager = userManager;
        _db = db;
        _currentUser = currentUser;
        _links = links;
    }

    public async Task<Outcome<List<UserListItem>>> ListAsync(UserRole? role, Guid? companyId, Guid? departmentId, Guid? subBranchId, CancellationToken ct)
    {
        var query = _userManager.Users.AsQueryable();
        if (role.HasValue) query = query.Where(u => u.Role == role.Value);
        if (companyId.HasValue) query = query.Where(u => u.CompanyId == companyId.Value);
        if (departmentId.HasValue) query = query.Where(u => u.DepartmentId == departmentId.Value);
        if (subBranchId.HasValue) query = query.Where(u => u.SubBranchId == subBranchId.Value);

        // Clients are limited to those with a ticket in the employee's branch. Staff are limited to the employee's own branch
        // too, except when the caller asks about one specific branch (e.g. picking who to assign a ticket to after routing it
        // there): staff can route tickets to any branch, so the people of that branch have to be visible to choose from.
        if (_currentUser.Role != UserRole.Admin)
        {
            var branchClients = await ClientVisibility.ClientsOfBranchAsync(_db, _currentUser, ct);
            query = departmentId.HasValue
                ? query.Where(u => u.Role != UserRole.Client || branchClients.Contains(u.Id))
                : query.VisibleTo(_currentUser, branchClients);
        }

        var users = await query.OrderBy(u => u.FirstName).ToListAsync(ct);
        var companyNames = await _db.Companies.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var departmentNames = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var subBranchNames = await _db.SubBranches.ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var result = users.Select(u => new UserListItem(
            u.Id, u.FirstName, u.LastName, u.Email!, u.Role.ToString(),
            u.CompanyId, u.CompanyId.HasValue && companyNames.TryGetValue(u.CompanyId.Value, out var n) ? n : null,
            u.PhoneNumber, u.IsActive, u.EmailConfirmed, u.CreatedAtUtc,
            u.DepartmentId, u.DepartmentId.HasValue && departmentNames.TryGetValue(u.DepartmentId.Value, out var dn) ? dn : null,
            u.SubBranchId, u.SubBranchId.HasValue && subBranchNames.TryGetValue(u.SubBranchId.Value, out var sn) ? sn : null,
            _links.ToPublicUrl(u.ProfilePictureUrl), u.LastLoginAtUtc)).ToList();

        return Outcome<List<UserListItem>>.Ok(result);
    }

    public async Task<Outcome<UserPageDto>> ListPagedAsync(string group, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var wantStaff = string.Equals(group, "staff", StringComparison.OrdinalIgnoreCase);

        // An employee sees the clients who have a ticket in their branch and the staff of their own branch; Admin sees everyone.
        var branchClients = await ClientVisibility.ClientsOfBranchAsync(_db, _currentUser, ct);
        var scope = _userManager.Users.AsNoTracking().VisibleTo(_currentUser, branchClients);

        var clientCount = await scope.CountAsync(u => u.Role == UserRole.Client, ct);
        var staffCount = await scope.CountAsync(u => u.Role != UserRole.Client, ct);

        var query = wantStaff ? scope.Where(u => u.Role != UserRole.Client) : scope.Where(u => u.Role == UserRole.Client);

        var text = search?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (text.Contains('@'))
            {
                var emailHash = _userManager.NormalizeEmail(text);
                query = query.Where(u => u.NormalizedEmail == emailHash);
            }
            else
            {
                var companyIds = await _db.Companies.Where(c => c.Name.Contains(text)).Select(c => c.Id).ToListAsync(ct);
                query = query.Where(u =>
                    (u.FirstName + " " + u.LastName).Contains(text) ||
                    (u.CompanyId != null && companyIds.Contains(u.CompanyId.Value)));
            }
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        // Names are looked up only for what is on this page.
        var companyIdsOnPage = users.Where(u => u.CompanyId.HasValue).Select(u => u.CompanyId!.Value).Distinct().ToList();
        var departmentIdsOnPage = users.Where(u => u.DepartmentId.HasValue).Select(u => u.DepartmentId!.Value).Distinct().ToList();
        var subBranchIdsOnPage = users.Where(u => u.SubBranchId.HasValue).Select(u => u.SubBranchId!.Value).Distinct().ToList();
        var companyNames = await _db.Companies.Where(c => companyIdsOnPage.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var departmentNames = await _db.Departments.Where(d => departmentIdsOnPage.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var subBranchNames = await _db.SubBranches.Where(s => subBranchIdsOnPage.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var items = users.Select(u => new UserListItem(
            u.Id, u.FirstName, u.LastName, u.Email!, u.Role.ToString(),
            u.CompanyId, u.CompanyId.HasValue && companyNames.TryGetValue(u.CompanyId.Value, out var n) ? n : null,
            u.PhoneNumber, u.IsActive, u.EmailConfirmed, u.CreatedAtUtc,
            u.DepartmentId, u.DepartmentId.HasValue && departmentNames.TryGetValue(u.DepartmentId.Value, out var dn) ? dn : null,
            u.SubBranchId, u.SubBranchId.HasValue && subBranchNames.TryGetValue(u.SubBranchId.Value, out var sn) ? sn : null,
            _links.ToPublicUrl(u.ProfilePictureUrl), u.LastLoginAtUtc)).ToList();

        return Outcome<UserPageDto>.Ok(new UserPageDto(items, page, pageSize, total, clientCount, staffCount));
    }

    public async Task<Outcome<UserListItem>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await _userManager.FindByIdAsync(id.ToString());
        if (u is null) return Outcome<UserListItem>.NotFound();

        // Branches are isolated: an employee may open staff of their own branch and clients who have a ticket in it.
        if (_currentUser.Role != UserRole.Admin)
        {
            var allowed = u.Role == UserRole.Client
                ? ClientVisibility.CanSeeClient(_db, _currentUser, u.Id)
                : u.DepartmentId == _currentUser.DepartmentId;
            if (!allowed) return Outcome<UserListItem>.NotFound();
        }

        string? companyName = u.CompanyId.HasValue
            ? await _db.Companies.Where(c => c.Id == u.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;
        string? departmentName = u.DepartmentId.HasValue
            ? await _db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;
        string? subBranchName = u.SubBranchId.HasValue
            ? await _db.SubBranches.Where(s => s.Id == u.SubBranchId).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;

        return Outcome<UserListItem>.Ok(new UserListItem(
            u.Id, u.FirstName, u.LastName, u.Email!, u.Role.ToString(),
            u.CompanyId, companyName, u.PhoneNumber, u.IsActive, u.EmailConfirmed, u.CreatedAtUtc,
            u.DepartmentId, departmentName, u.SubBranchId, subBranchName,
            _links.ToPublicUrl(u.ProfilePictureUrl), u.LastLoginAtUtc));
    }

    public async Task<Outcome<ClientLookupResult>> LookupByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return Outcome<ClientLookupResult>.Invalid("Email is required.");

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || user.Role != UserRole.Client)
            return Outcome<ClientLookupResult>.NotFound("No client account found with that email.");

        var companyName = user.CompanyId.HasValue
            ? await _db.Companies.Where(c => c.Id == user.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;

        // Opening a ticket on behalf of a client who has not written to this branch before needs this lookup, so it stays: but
        // it requires knowing the exact address (it cannot be used to browse), a client from outside the branch is returned
        // without their phone number, and each such lookup is recorded.
        var inMyBranch = ClientVisibility.CanSeeClient(_db, _currentUser, user.Id);
        if (!inMyBranch)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = _currentUser.UserId,
                Action = "ClientLookupOutsideBranch",
                EntityType = nameof(ApplicationUser),
                EntityId = user.Id
            });
            await _db.SaveChangesAsync(ct);
        }

        return Outcome<ClientLookupResult>.Ok(new ClientLookupResult(user.Id, user.FirstName, user.LastName, user.Email!,
            inMyBranch ? user.PhoneNumber : null, user.CompanyId, companyName));
    }
}
