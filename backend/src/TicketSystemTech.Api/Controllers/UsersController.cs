using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Services;
using TicketSystemTech.Api.Users;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Api.Controllers;

/// <summary>The HTTP face of the accounts: it reads the request, hands it to the service that owns the rule and turns the answer into a status code.</summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserQueries _queries;
    private readonly IUserAccountService _accounts;
    private readonly IUserPrivacyService _privacy;
    private readonly IUserProfileService _profile;

    public UsersController(IUserQueries queries, IUserAccountService accounts, IUserPrivacyService privacy, IUserProfileService profile)
    {
        _queries = queries;
        _accounts = accounts;
        _privacy = privacy;
        _profile = profile;
    }

    /// <summary>Admin-only: create Admin/Employee accounts.</summary>
    [HttpPost("employees")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<CreatedUserResponse>> CreateEmployee(CreateEmployeeRequest request) =>
        this.ToActionResult(await _accounts.CreateEmployeeAsync(request));

    /// <summary>Admin or Employee: create a Client account tied to an existing company.</summary>
    [HttpPost("clients")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CreatedUserResponse>> CreateClient(CreateClientRequest request) =>
        this.ToActionResult(await _accounts.CreateClientAsync(request));

    /// <summary>Regenerates a fresh short-lived temporary password (e.g. if the previous one expired unused).</summary>
    [HttpPost("{id:guid}/regenerate-temp-password")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<CreatedUserResponse>> RegenerateTempPassword(Guid id) =>
        this.ToActionResult(await _accounts.RegenerateTempPasswordAsync(id));

    /// <summary>
    /// Admin-only: updates an existing account's editable profile fields — everything except email
    /// (email is the login identity and isn't changeable after creation).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> UpdateUser(Guid id, UpdateUserRequest request) =>
        this.ToResult(await _accounts.UpdateAsync(id, request));

    /// <summary>
    /// Admin-only: marks an account active/inactive. Inactive means "no longer collaborating" —
    /// the account can't log in (enforced in AuthController.Login) and stops receiving any app emails
    /// (enforced centrally by the e-mail outbox worker). Can be reversed at any time.
    /// </summary>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> SetActive(Guid id, SetUserActiveRequest request) =>
        this.ToResult(await _accounts.SetActiveAsync(id, request.IsActive));

    /// <summary>
    /// Admin-only: everything the system holds about one person, as a single JSON file (the right of access and portability). The
    /// text of the messages they wrote is included, because it is theirs; what other people wrote about them is not.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ExportUserData(Guid id, CancellationToken ct)
    {
        var export = await _privacy.ExportAsync(id, ct);
        if (export.Succeeded) Response.Headers.ContentDisposition = $"attachment; filename=\"{export.Value!.FileName}\"";
        return this.ToResult(export, e => e.Data);
    }

    /// <summary>
    /// Admin-only: removes what identifies a person and keeps what the work needs. The name becomes "Obrisan korisnik", the e-mail
    /// address a placeholder nobody can receive mail at, the telephone number and the picture are deleted, the password is
    /// removed, the account is locked for good and every session ends. Tickets, messages and work time stay, so the history of
    /// the work is still true; they now carry the placeholder name. It cannot be undone. The audit log keeps that it happened,
    /// without any of the personal data. Not possible for yourself, so there is always an Admin left to do it.
    /// The name is also taken out of the sentences the application itself wrote about the person ("Ticket closed by ...",
    /// "... assigned you a task"): those are generated text, not somebody's own words, so replacing the name in them is safe.
    /// What it does not reach: names that other people typed into the text of their own messages, and copies in backups.
    /// </summary>
    [HttpPost("{id:guid}/anonymize")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> AnonymizeUser(Guid id) =>
        this.ToResult(await _privacy.AnonymizeAsync(id));

    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<List<UserListItem>>> List([FromQuery] UserRole? role, [FromQuery] Guid? companyId, [FromQuery] Guid? departmentId, [FromQuery] Guid? subBranchId, CancellationToken ct) =>
        this.ToActionResult(await _queries.ListAsync(role, companyId, departmentId, subBranchId, ct));

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
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        this.ToActionResult(await _queries.ListPagedAsync(group, search, page, pageSize, ct));

    /// <summary>Fetches a single account's profile — used by the user detail page.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<UserListItem>> GetById(Guid id, CancellationToken ct) =>
        this.ToActionResult(await _queries.GetAsync(id, ct));

    /// <summary>Uploads a profile picture. A user can set their own; Admin can set anyone's.</summary>
    [HttpPost("{id:guid}/profile-picture")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfilePicture(Guid id, [FromForm] ProfilePictureForm form) =>
        this.ToResult(await _profile.UploadPictureAsync(id, form.File));

    /// <summary>
    /// Looks up a Client account by email so staff can pre-fill their info when opening a ticket
    /// on behalf of a client who called/reported the issue verbally (not through the client portal).
    /// </summary>
    [HttpGet("lookup-by-email")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Employee)}")]
    public async Task<ActionResult<ClientLookupResult>> LookupByEmail([FromQuery] string email, CancellationToken ct) =>
        this.ToActionResult(await _queries.LookupByEmailAsync(email, ct));

    [HttpPatch("me/phone")]
    [Authorize(Roles = nameof(UserRole.Client))]
    public async Task<IActionResult> SetMyPhone(SetPhoneNumberRequest request) =>
        this.ToResult(await _profile.SetMyPhoneAsync(request.PhoneNumber));

    /// <summary>The language the person chose in the application, so that the e-mails to them are written in it. Any signed-in person.</summary>
    [HttpPut("me/language")]
    public async Task<IActionResult> SetMyLanguage(SetLanguageRequest request) =>
        this.ToResult(await _profile.SetMyLanguageAsync(request.Language));

    /// <summary>Which e-mails about tickets and tasks this person gets (the ones their role is sent, each on or off). Any signed-in person.</summary>
    [HttpGet("me/notification-preferences")]
    public async Task<ActionResult<List<NotificationPreferenceItem>>> GetMyNotificationPreferences(CancellationToken ct) =>
        this.ToActionResult(await _profile.GetMyNotificationPreferencesAsync(ct));

    [HttpPut("me/notification-preferences")]
    public async Task<ActionResult<List<NotificationPreferenceItem>>> SetMyNotificationPreferences(SetNotificationPreferencesRequest request) =>
        this.ToActionResult(await _profile.SetMyNotificationPreferencesAsync(request));

    [HttpPost("me/skip-phone-prompt")]
    [Authorize(Roles = nameof(UserRole.Client))]
    public async Task<IActionResult> SkipPhonePrompt() =>
        this.ToResult(await _profile.SkipPhonePromptAsync());
}
