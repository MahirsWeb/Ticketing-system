using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Api.WorkTasks;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.Users;

/// <summary>Making and changing accounts: creating them, resetting a password, editing, switching on and off.</summary>
public interface IUserAccountService
{
    /// <summary>Admin-only: create Admin/Employee accounts.</summary>
    Task<Outcome<CreatedUserResponse>> CreateEmployeeAsync(CreateEmployeeRequest request);

    /// <summary>Admin or Employee: create a Client account tied to an existing company.</summary>
    Task<Outcome<CreatedUserResponse>> CreateClientAsync(CreateClientRequest request);

    /// <summary>Regenerates a fresh short-lived temporary password (e.g. if the previous one expired unused).</summary>
    Task<Outcome<CreatedUserResponse>> RegenerateTempPasswordAsync(Guid id);

    /// <summary>Admin-only: updates an existing account's editable profile fields: everything except email (the login identity).</summary>
    Task<Outcome<Done>> UpdateAsync(Guid id, UpdateUserRequest request);

    /// <summary>Admin-only: marks an account active/inactive. Inactive means "no longer collaborating"; it can be reversed at any time.</summary>
    Task<Outcome<Done>> SetActiveAsync(Guid id, bool isActive);
}

public sealed class UserAccountService : IUserAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITemporaryPasswordGenerator _tempPasswordGenerator;
    private readonly TemporaryPasswordOptions _tempPasswordOptions;
    private readonly FrontendOptions _frontendOptions;
    private readonly IMemoryCache _cache;
    private readonly ITicketAssignmentService _assignments;
    private readonly IBranchRules _branches;
    private readonly INotifier _notifier;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public UserAccountService(
        UserManager<ApplicationUser> userManager, AppDbContext db, ICurrentUserService currentUser,
        ITemporaryPasswordGenerator tempPasswordGenerator, IOptions<TemporaryPasswordOptions> tempPasswordOptions,
        IOptions<FrontendOptions> frontendOptions, IMemoryCache cache,
        ITicketAssignmentService assignments, IBranchRules branches, INotifier notifier, TimeProvider time)
    {
        _userManager = userManager;
        _db = db;
        _currentUser = currentUser;
        _tempPasswordGenerator = tempPasswordGenerator;
        _tempPasswordOptions = tempPasswordOptions.Value;
        _frontendOptions = frontendOptions.Value;
        _cache = cache;
        _assignments = assignments;
        _branches = branches;
        _notifier = notifier;
        _time = time;
    }

    public async Task<Outcome<CreatedUserResponse>> CreateEmployeeAsync(CreateEmployeeRequest request)
    {
        if (request.Role == UserRole.Client)
            return Outcome<CreatedUserResponse>.Invalid("Use POST /api/users/clients to create client accounts.");

        Guid? subBranchId = null;
        if (request.Role == UserRole.Employee)
        {
            if (request.DepartmentId is null)
                return Outcome<CreatedUserResponse>.Invalid("A branch must be selected for Employee accounts.");
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId.Value && d.IsActive);
            if (!departmentExists)
                return Outcome<CreatedUserResponse>.Invalid("Branch not found.");

            var subBranchError = await _branches.ValidateSubBranchAsync(request.DepartmentId.Value, request.SubBranchId);
            if (subBranchError is not null) return Outcome<CreatedUserResponse>.Invalid(subBranchError);
            subBranchId = request.SubBranchId;
        }

        return await CreateUserInternalAsync(request.FirstName, request.LastName, request.Email, request.Role, companyId: null,
            departmentId: request.Role == UserRole.Employee ? request.DepartmentId : null,
            subBranchId: subBranchId);
    }

    public async Task<Outcome<CreatedUserResponse>> CreateClientAsync(CreateClientRequest request)
    {
        var companyExists = await _db.Companies.AnyAsync(c => c.Id == request.CompanyId);
        if (!companyExists)
            return Outcome<CreatedUserResponse>.Invalid("Company not found.");

        return await CreateUserInternalAsync(request.FirstName, request.LastName, request.Email, UserRole.Client, request.CompanyId, departmentId: null, subBranchId: null);
    }

    public async Task<Outcome<CreatedUserResponse>> RegenerateTempPasswordAsync(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Outcome<CreatedUserResponse>.NotFound();

        // Admin may reset anyone. A non-admin employee may only reset a Client, and only one who actually has
        // a ticket in the employee's own branch — otherwise any employee could take over any client account
        // (including one belonging to a different branch's customers) by resetting its password.
        if (_currentUser.Role != UserRole.Admin)
        {
            if (user.Role != UserRole.Client) return Outcome<CreatedUserResponse>.Forbidden();

            var departmentId = _currentUser.DepartmentId;
            var hasTicketInMyBranch = departmentId is not null &&
                await _db.Tickets.AnyAsync(t => t.ClientId == user.Id && t.DepartmentId == departmentId);
            if (!hasTicketInMyBranch) return Outcome<CreatedUserResponse>.Forbidden();
        }

        var tempPassword = _tempPasswordGenerator.Generate();
        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
            return Outcome<CreatedUserResponse>.Invalid("Could not reset password.");

        var addResult = await _userManager.AddPasswordAsync(user, tempPassword);
        if (!addResult.Succeeded)
            return Outcome<CreatedUserResponse>.Invalid(string.Join(" ", addResult.Errors.Select(e => e.Description)));

        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresAtUtc = UtcNow.AddMinutes(_tempPasswordOptions.ValidityMinutes);
        await _userManager.UpdateAsync(user);
        _cache.Remove(UserStatusCache.Key(user.Id)); // the new security stamp must take effect now, not after the cache TTL

        // Who reset whose password and when — the password itself is never written to the log.
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Action = "RegenerateTempPassword",
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Details = $"Role of target: {user.Role}"
        });
        // The mail that carries the password is filed in the same save as the record of who asked for it.
        var mail = AccountMessages.NewTemporaryPassword(user.FirstName, user.Email!, tempPassword, _tempPasswordOptions.ValidityMinutes, $"{_frontendOptions.BaseUrl}/login");
        _db.StageEmail(user.Email!, mail.Subject, mail.Html);
        await _db.SaveChangesAsync();

        // Only an Admin gets to see the plaintext. A non-admin employee resetting a client's password must not
        // learn it: it travels by email to the account owner, so the employee cannot sign in as the client.
        var visiblePassword = _currentUser.Role == UserRole.Admin ? tempPassword : null;
        return Outcome<CreatedUserResponse>.Ok(new CreatedUserResponse(user.Id, user.Email!, visiblePassword, user.TemporaryPasswordExpiresAtUtc.Value));
    }

    public async Task<Outcome<Done>> UpdateAsync(Guid id, UpdateUserRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return Outcome<Done>.NotFound();

        if (user.Id == _currentUser.UserId && request.Role != user.Role)
            return Outcome<Done>.Invalid("You cannot change your own account type.");

        Guid? companyId = null;
        Guid? departmentId = null;
        Guid? subBranchId = null;

        if (request.Role == UserRole.Client)
        {
            if (request.CompanyId is null)
                return Outcome<Done>.Invalid("A company must be selected for Client accounts.");
            var companyExists = await _db.Companies.AnyAsync(c => c.Id == request.CompanyId.Value);
            if (!companyExists) return Outcome<Done>.Invalid("Company not found.");
            companyId = request.CompanyId;
        }
        else if (request.Role == UserRole.Employee)
        {
            if (request.DepartmentId is null)
                return Outcome<Done>.Invalid("A branch must be selected for Employee accounts.");
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId.Value && d.IsActive);
            if (!departmentExists) return Outcome<Done>.Invalid("Branch not found.");

            var subBranchError = await _branches.ValidateSubBranchAsync(request.DepartmentId.Value, request.SubBranchId);
            if (subBranchError is not null) return Outcome<Done>.Invalid(subBranchError);
            departmentId = request.DepartmentId;
            subBranchId = request.SubBranchId;
        }

        // Captured before the change: a staff member who leaves their role, branch or sub-branch can no longer be
        // the assignee of the tickets they were working there.
        var wasEmployee = user.Role == UserRole.Employee;
        var movedAway = wasEmployee && (request.Role != UserRole.Employee || user.DepartmentId != departmentId || user.SubBranchId != subBranchId);
        // Tasks belong to a branch: they are handed back when the person leaves the branch or the role, not for a move between its sub-branches.
        var leftBranchOrRole = wasEmployee && (request.Role != UserRole.Employee || user.DepartmentId != departmentId);

        _db.Audit(_currentUser.UserId, "UserUpdated", nameof(ApplicationUser), user.Id,
            $"Role: {user.Role} -> {request.Role}; branch: {user.DepartmentId?.ToString() ?? "-"} -> {departmentId?.ToString() ?? "-"}; " +
            $"sub-branch: {user.SubBranchId?.ToString() ?? "-"} -> {subBranchId?.ToString() ?? "-"}; company: {user.CompanyId?.ToString() ?? "-"} -> {companyId?.ToString() ?? "-"}");

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Role = request.Role;
        user.CompanyId = companyId;
        user.DepartmentId = departmentId;
        user.SubBranchId = subBranchId;

        // The tickets are released BEFORE the person is saved. The release saves what is pending in the same context, the changes to this
        // person included, so either both happen or neither does: if a ticket in use by someone else makes the save fail (a 409), nothing
        // has changed and the Admin simply tries again. Saving the person first used to leave them moved while their tickets stayed theirs.
        IReadOnlyList<TaskAssignedEvent> handedBack = Array.Empty<TaskAssignedEvent>();
        if (movedAway)
        {
            await _assignments.ReleaseLiveTicketsAsync(user.Id, "moved to a different role or branch", _currentUser.UserId);
            await _db.EndRunningTaskSessionsAsync(null, user.Id, UtcNow); // their task timers stop too, not just ticket work
            if (leftBranchOrRole)
                handedBack = await _db.HandBackOpenTasksAsync(_notifier, user.Id, _currentUser.UserId, "owner moved to a different role or branch");
        }
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded) return Outcome<Done>.Invalid(string.Join(" ", saved.Errors.Select(e => e.Description)));
        _cache.Remove(UserStatusCache.Key(user.Id));
        foreach (var task in handedBack) await _notifier.DeliverAsync(task);

        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<Outcome<Done>> SetActiveAsync(Guid id, bool isActive)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return Outcome<Done>.NotFound();
        if (user.Id == _currentUser.UserId) return Outcome<Done>.Invalid("You cannot deactivate your own account.");

        _db.Audit(_currentUser.UserId, isActive ? "UserActivated" : "UserDeactivated", nameof(ApplicationUser), user.Id, $"Role of target: {user.Role}");
        user.IsActive = isActive;
        // A deactivated person's sign-ins end for good: if the account is switched on again later, a cookie that was never shown in the meantime must
        // not start working again.
        if (!isActive) await _db.RevokeRefreshTokensAsync(user.Id, UtcNow);

        // Someone who can no longer work must not keep owning live tickets. Released first and saved together with the person (see
        // UpdateUser): a failure leaves the account as it was instead of inactive with its tickets still on it.
        IReadOnlyList<TaskAssignedEvent> handedBack = Array.Empty<TaskAssignedEvent>();
        if (!isActive && user.Role == UserRole.Employee)
        {
            await _assignments.ReleaseLiveTicketsAsync(user.Id, "account deactivated", _currentUser.UserId);
            await _db.EndRunningTaskSessionsAsync(null, user.Id, UtcNow); // a deactivated person's running task timer must stop
            handedBack = await _db.HandBackOpenTasksAsync(_notifier, user.Id, _currentUser.UserId, "account deactivated");
        }
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded) return Outcome<Done>.Invalid(string.Join(" ", saved.Errors.Select(e => e.Description)));
        _cache.Remove(UserStatusCache.Key(user.Id));
        foreach (var task in handedBack) await _notifier.DeliverAsync(task);

        return Outcome<Done>.Ok(Done.Value);
    }

    private async Task<Outcome<CreatedUserResponse>> CreateUserInternalAsync(string firstName, string lastName, string email, UserRole role, Guid? companyId, Guid? departmentId, Guid? subBranchId)
    {
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return Outcome<CreatedUserResponse>.Conflict("A user with this email already exists.");

        var tempPassword = _tempPasswordGenerator.Generate();
        var expiresAt = UtcNow.AddMinutes(_tempPasswordOptions.ValidityMinutes);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            CompanyId = companyId,
            DepartmentId = departmentId,
            SubBranchId = subBranchId,
            IsActive = true,
            MustChangePassword = true,
            TemporaryPasswordExpiresAtUtc = expiresAt,
            // Employees are provisioned by a trusted admin; only clients go through email verification.
            EmailConfirmed = role != UserRole.Client
        };

        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            return Outcome<CreatedUserResponse>.Invalid(string.Join(" ", result.Errors.Select(e => e.Description)));

        _db.Audit(_currentUser.UserId, "UserCreated", nameof(ApplicationUser), user.Id, $"Role: {role}");
        var invitation = AccountMessages.Invite(firstName, email, tempPassword, _tempPasswordOptions.ValidityMinutes, $"{_frontendOptions.BaseUrl}/login");
        _db.StageEmail(email, invitation.Subject, invitation.Html);
        await _db.SaveChangesAsync();

        // The plaintext temporary password is shown to an Admin only. A non-admin employee (who may create Client accounts)
        // does not get it back: it travels to the new account's own mailbox by e-mail, so the employee cannot sign in as the
        // client before they set their own password. This matches how regenerating a temporary password already behaves.
        var visiblePassword = _currentUser.Role == UserRole.Admin ? tempPassword : null;
        return Outcome<CreatedUserResponse>.Ok(new CreatedUserResponse(user.Id, email, visiblePassword, expiresAt));
    }
}
