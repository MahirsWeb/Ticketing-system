using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITemporaryPasswordGenerator _tempPasswordGenerator;
    private readonly TemporaryPasswordOptions _tempPasswordOptions;
    private readonly IEmailSender _emailSender;
    private readonly FrontendOptions _frontendOptions;
    private readonly IFileStorage _fileStorage;
    private readonly IMemoryCache _cache;
    private readonly ITicketAssignmentService _assignments;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        ICurrentUserService currentUser,
        ITemporaryPasswordGenerator tempPasswordGenerator,
        IOptions<TemporaryPasswordOptions> tempPasswordOptions,
        IEmailSender emailSender,
        IOptions<FrontendOptions> frontendOptions,
        IFileStorage fileStorage,
        IMemoryCache cache,
        ITicketAssignmentService assignments)
    {
        _assignments = assignments;
        _userManager = userManager;
        _db = db;
        _currentUser = currentUser;
        _tempPasswordGenerator = tempPasswordGenerator;
        _tempPasswordOptions = tempPasswordOptions.Value;
        _emailSender = emailSender;
        _cache = cache;
        _frontendOptions = frontendOptions.Value;
        _fileStorage = fileStorage;
    }

    /// <summary>Admin-only: create Admin/Employee accounts.</summary>
    [HttpPost("employees")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CreatedUserResponse>> CreateEmployee(CreateEmployeeRequest request)
    {
        if (request.Role == UserRole.Client)
            return BadRequest(new { message = "Use POST /api/users/clients to create client accounts." });

        Guid? subBranchId = null;
        if (request.Role == UserRole.Employee)
        {
            if (request.DepartmentId is null)
                return BadRequest(new { message = "A branch must be selected for Employee accounts." });
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId.Value && d.IsActive);
            if (!departmentExists)
                return BadRequest(new { message = "Branch not found." });

            var subBranchError = await ValidateSubBranchAsync(request.DepartmentId.Value, request.SubBranchId);
            if (subBranchError is not null) return BadRequest(new { message = subBranchError });
            subBranchId = request.SubBranchId;
        }

        return await CreateUserInternal(request.FirstName, request.LastName, request.Email, request.Role, companyId: null,
            departmentId: request.Role == UserRole.Employee ? request.DepartmentId : null,
            subBranchId: subBranchId);
    }

    /// <summary>A branch with sub-branches requires picking exactly one of them; a branch without any must not receive one.</summary>
    private async Task<string?> ValidateSubBranchAsync(Guid departmentId, Guid? subBranchId)
    {
        var activeSubBranches = await _db.SubBranches.Where(s => s.DepartmentId == departmentId && s.IsActive).ToListAsync();
        if (activeSubBranches.Count == 0)
            return subBranchId.HasValue ? "This branch has no sub-branches." : null;

        if (subBranchId is null) return "A sub-branch must be selected for this branch.";
        if (!activeSubBranches.Any(s => s.Id == subBranchId.Value)) return "Sub-branch not found for this branch.";
        return null;
    }

    /// <summary>Admin or Employee: create a Client account tied to an existing company.</summary>
    [HttpPost("clients")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CreatedUserResponse>> CreateClient(CreateClientRequest request)
    {
        var companyExists = await _db.Companies.AnyAsync(c => c.Id == request.CompanyId);
        if (!companyExists)
            return BadRequest(new { message = "Company not found." });

        return await CreateUserInternal(request.FirstName, request.LastName, request.Email, UserRole.Client, request.CompanyId, departmentId: null, subBranchId: null);
    }

    /// <summary>Regenerates a fresh short-lived temporary password (e.g. if the previous one expired unused).</summary>
    [HttpPost("{id:guid}/regenerate-temp-password")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CreatedUserResponse>> RegenerateTempPassword(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        // Admin may reset anyone. A non-admin employee may only reset a Client, and only one who actually has
        // a ticket in the employee's own branch — otherwise any employee could take over any client account
        // (including one belonging to a different branch's customers) by resetting its password.
        if (_currentUser.Role != UserRole.Admin)
        {
            if (user.Role != UserRole.Client) return Forbid();

            var departmentId = _currentUser.DepartmentId;
            var hasTicketInMyBranch = departmentId is not null &&
                await _db.Tickets.AnyAsync(t => t.ClientId == user.Id && t.DepartmentId == departmentId);
            if (!hasTicketInMyBranch) return Forbid();
        }

        var tempPassword = _tempPasswordGenerator.Generate();
        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
            return BadRequest(new { message = "Could not reset password." });

        var addResult = await _userManager.AddPasswordAsync(user, tempPassword);
        if (!addResult.Succeeded)
            return BadRequest(new { message = string.Join(" ", addResult.Errors.Select(e => e.Description)) });

        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(_tempPasswordOptions.ValidityMinutes);
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
        await _db.SaveChangesAsync();

        await _emailSender.SendAsync(user.Email!, "Your new temporary password — Ticket System Tech",
            EmailTemplates.Invite(user.FirstName, user.Email!, tempPassword, _tempPasswordOptions.ValidityMinutes, $"{_frontendOptions.BaseUrl}/login"));

        // Only an Admin gets to see the plaintext. A non-admin employee resetting a client's password must not
        // learn it: it travels by email to the account owner, so the employee cannot sign in as the client.
        var visiblePassword = _currentUser.Role == UserRole.Admin ? tempPassword : null;
        return Ok(new CreatedUserResponse(user.Id, user.Email!, visiblePassword, user.TemporaryPasswordExpiresAtUtc.Value));
    }

    /// <summary>
    /// Admin-only: updates an existing account's editable profile fields — everything except email
    /// (email is the login identity and isn't changeable after creation).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> UpdateUser(Guid id, UpdateUserRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        if (user.Id == _currentUser.UserId && request.Role != user.Role)
            return BadRequest(new { message = "You cannot change your own account type." });

        Guid? companyId = null;
        Guid? departmentId = null;
        Guid? subBranchId = null;

        if (request.Role == UserRole.Client)
        {
            if (request.CompanyId is null)
                return BadRequest(new { message = "A company must be selected for Client accounts." });
            var companyExists = await _db.Companies.AnyAsync(c => c.Id == request.CompanyId.Value);
            if (!companyExists) return BadRequest(new { message = "Company not found." });
            companyId = request.CompanyId;
        }
        else if (request.Role == UserRole.Employee)
        {
            if (request.DepartmentId is null)
                return BadRequest(new { message = "A branch must be selected for Employee accounts." });
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId.Value && d.IsActive);
            if (!departmentExists) return BadRequest(new { message = "Branch not found." });

            var subBranchError = await ValidateSubBranchAsync(request.DepartmentId.Value, request.SubBranchId);
            if (subBranchError is not null) return BadRequest(new { message = subBranchError });
            departmentId = request.DepartmentId;
            subBranchId = request.SubBranchId;
        }

        // Captured before the change: a staff member who leaves their role, branch or sub-branch can no longer be
        // the assignee of the tickets they were working there.
        var wasEmployee = user.Role == UserRole.Employee;
        var movedAway = wasEmployee && (request.Role != UserRole.Employee || user.DepartmentId != departmentId || user.SubBranchId != subBranchId);

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Role = request.Role;
        user.CompanyId = companyId;
        user.DepartmentId = departmentId;
        user.SubBranchId = subBranchId;
        await _userManager.UpdateAsync(user);
        _cache.Remove(UserStatusCache.Key(user.Id));

        if (movedAway)
            await _assignments.ReleaseLiveTicketsAsync(user.Id, "moved to a different role or branch", _currentUser.UserId);

        return Ok();
    }

    /// <summary>
    /// Admin-only: marks an account active/inactive. Inactive means "no longer collaborating" —
    /// the account can't log in (enforced in AuthController.Login) and stops receiving any app emails
    /// (enforced centrally in BrevoEmailSender). Can be reversed at any time.
    /// </summary>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> SetActive(Guid id, SetUserActiveRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        if (user.Id == _currentUser.UserId) return BadRequest(new { message = "You cannot deactivate your own account." });

        user.IsActive = request.IsActive;
        await _userManager.UpdateAsync(user);
        _cache.Remove(UserStatusCache.Key(user.Id));

        // Someone who can no longer work must not keep owning live tickets.
        if (!request.IsActive && user.Role == UserRole.Employee)
            await _assignments.ReleaseLiveTicketsAsync(user.Id, "account deactivated", _currentUser.UserId);

        return Ok();
    }

    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<UserListItem>>> List([FromQuery] UserRole? role, [FromQuery] Guid? companyId, [FromQuery] Guid? departmentId, [FromQuery] Guid? subBranchId)
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
            var branchClients = await ClientVisibility.ClientsOfBranchAsync(_db, _currentUser, HttpContext.RequestAborted);
            query = departmentId.HasValue
                ? query.Where(u => u.Role != UserRole.Client || branchClients.Contains(u.Id))
                : query.VisibleTo(_currentUser, branchClients);
        }

        var users = await query.OrderBy(u => u.FirstName).ToListAsync();
        var companyNames = await _db.Companies.ToDictionaryAsync(c => c.Id, c => c.Name);
        var departmentNames = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name);
        var subBranchNames = await _db.SubBranches.ToDictionaryAsync(s => s.Id, s => s.Name);

        var result = users.Select(u => new UserListItem(
            u.Id, u.FirstName, u.LastName, u.Email!, u.Role.ToString(),
            u.CompanyId, u.CompanyId.HasValue && companyNames.TryGetValue(u.CompanyId.Value, out var n) ? n : null,
            u.PhoneNumber, u.IsActive, u.EmailConfirmed, u.CreatedAtUtc,
            u.DepartmentId, u.DepartmentId.HasValue && departmentNames.TryGetValue(u.DepartmentId.Value, out var dn) ? dn : null,
            u.SubBranchId, u.SubBranchId.HasValue && subBranchNames.TryGetValue(u.SubBranchId.Value, out var sn) ? sn : null,
            u.ProfilePictureUrl, u.LastLoginAtUtc)).ToList();

        return Ok(result);
    }

    /// <summary>
    /// The Users screen's list: one page of clients or of staff, optionally filtered by a search text. The plain
    /// <c>GET /api/users</c> loads and decrypts every account on every call, which was fine for the 39 staff the pickers
    /// ask for and wrong for ~2,900 clients.
    ///
    /// Search matches names and the company name as a substring. Email is stored encrypted, so it can only be matched
    /// exactly: type a full address to find that account.
    /// </summary>
    [HttpGet("paged")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<UserPageDto>> ListPaged(
        [FromQuery] string group = "clients", [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
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
            u.ProfilePictureUrl, u.LastLoginAtUtc)).ToList();

        return Ok(new UserPageDto(items, page, pageSize, total, clientCount, staffCount));
    }

    /// <summary>Fetches a single account's profile — used by the user detail page.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<UserListItem>> GetById(Guid id)
    {
        var u = await _userManager.FindByIdAsync(id.ToString());
        if (u is null) return NotFound();

        // Branches are isolated: an employee may open staff of their own branch and clients who have a ticket in it.
        if (_currentUser.Role != UserRole.Admin)
        {
            var allowed = u.Role == UserRole.Client
                ? ClientVisibility.CanSeeClient(_db, _currentUser, u.Id)
                : u.DepartmentId == _currentUser.DepartmentId;
            if (!allowed) return NotFound();
        }

        string? companyName = u.CompanyId.HasValue
            ? await _db.Companies.Where(c => c.Id == u.CompanyId).Select(c => c.Name).FirstOrDefaultAsync()
            : null;
        string? departmentName = u.DepartmentId.HasValue
            ? await _db.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync()
            : null;
        string? subBranchName = u.SubBranchId.HasValue
            ? await _db.SubBranches.Where(s => s.Id == u.SubBranchId).Select(s => s.Name).FirstOrDefaultAsync()
            : null;

        return Ok(new UserListItem(
            u.Id, u.FirstName, u.LastName, u.Email!, u.Role.ToString(),
            u.CompanyId, companyName, u.PhoneNumber, u.IsActive, u.EmailConfirmed, u.CreatedAtUtc,
            u.DepartmentId, departmentName, u.SubBranchId, subBranchName,
            u.ProfilePictureUrl, u.LastLoginAtUtc));
    }

    private const long MaxProfilePictureSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    /// <summary>Uploads a profile picture. A user can set their own; Admin can set anyone's.</summary>
    [HttpPost("{id:guid}/profile-picture")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfilePicture(Guid id, [FromForm] ProfilePictureForm form)
    {
        if (_currentUser.UserId != id && _currentUser.Role != UserRole.Admin) return Forbid();
        var file = form.File;

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        if (file is null || file.Length == 0) return BadRequest(new { message = "No file was provided." });
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedImageExtensions.Contains(extension)) return BadRequest(new { message = $"File type '{extension}' is not allowed." });
        if (file.Length > MaxProfilePictureSizeBytes) return BadRequest(new { message = "Image exceeds the 5 MB limit." });

        await using var stream = file.OpenReadStream();
        var url = await _fileStorage.SavePublicImageAsync(file.FileName, file.ContentType, stream);

        var previousPicture = user.ProfilePictureUrl;
        user.ProfilePictureUrl = url;
        await _userManager.UpdateAsync(user);
        _fileStorage.DeletePublicImage(previousPicture); // the old picture is no longer referenced by anything
        return Ok(new { profilePictureUrl = url });
    }

    /// <summary>
    /// Looks up a Client account by email so staff can pre-fill their info when opening a ticket
    /// on behalf of a client who called/reported the issue verbally (not through the client portal).
    /// </summary>
    [HttpGet("lookup-by-email")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<ClientLookupResult>> LookupByEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Email is required." });

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || user.Role != UserRole.Client)
            return NotFound(new { message = "No client account found with that email." });

        var companyName = user.CompanyId.HasValue
            ? await _db.Companies.Where(c => c.Id == user.CompanyId).Select(c => c.Name).FirstOrDefaultAsync()
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
            await _db.SaveChangesAsync();
        }

        return Ok(new ClientLookupResult(user.Id, user.FirstName, user.LastName, user.Email!,
            inMyBranch ? user.PhoneNumber : null, user.CompanyId, companyName));
    }

    [HttpPatch("me/phone")]
    [Authorize(Roles = nameof(UserRole.Client))]
    public async Task<IActionResult> SetMyPhone(SetPhoneNumberRequest request)
    {
        if (_currentUser.UserId is null) return Unauthorized();
        var user = await _userManager.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null) return Unauthorized();

        user.PhoneNumber = request.PhoneNumber;
        user.PhoneNumberPrompted = true;
        await _userManager.UpdateAsync(user);
        return Ok(new { message = "Phone number saved." });
    }

    [HttpPost("me/skip-phone-prompt")]
    [Authorize(Roles = nameof(UserRole.Client))]
    public async Task<IActionResult> SkipPhonePrompt()
    {
        if (_currentUser.UserId is null) return Unauthorized();
        var user = await _userManager.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null) return Unauthorized();

        user.PhoneNumberPrompted = true;
        await _userManager.UpdateAsync(user);
        return Ok();
    }

    private async Task<ActionResult<CreatedUserResponse>> CreateUserInternal(string firstName, string lastName, string email, UserRole role, Guid? companyId, Guid? departmentId, Guid? subBranchId)
    {
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return Conflict(new { message = "A user with this email already exists." });

        var tempPassword = _tempPasswordGenerator.Generate();
        var expiresAt = DateTime.UtcNow.AddMinutes(_tempPasswordOptions.ValidityMinutes);

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
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        await _emailSender.SendAsync(email, "You've been invited to Ticket System Tech",
            EmailTemplates.Invite(firstName, email, tempPassword, _tempPasswordOptions.ValidityMinutes, $"{_frontendOptions.BaseUrl}/login"));

        return Ok(new CreatedUserResponse(user.Id, email, tempPassword, expiresAt));
    }
}
