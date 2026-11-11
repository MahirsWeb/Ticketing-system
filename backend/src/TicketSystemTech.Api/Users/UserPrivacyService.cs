using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Contracts;
using TicketSystemTech.Api.Emails;
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

/// <summary>What a person's data export is: the content, and the file name it should be offered under.</summary>
public sealed record UserExport(object Data, string FileName);

/// <summary>The rights of a person over their data: to receive it, and to have what identifies them removed.</summary>
public interface IUserPrivacyService
{
    /// <summary>
    /// Admin-only: everything the system holds about one person, as a single JSON file (the right of access and portability). The
    /// text of the messages they wrote is included, because it is theirs; what other people wrote about them is not.
    /// </summary>
    Task<Outcome<UserExport>> ExportAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Admin-only: removes what identifies a person and keeps what the work needs. It cannot be undone. Not possible for yourself,
    /// so there is always an Admin left to do it. What it does not reach: names that other people typed into the text of their own
    /// messages, and copies in backups.
    /// </summary>
    Task<Outcome<Done>> AnonymizeAsync(Guid id);
}

public sealed class UserPrivacyService : IUserPrivacyService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemoryCache _cache;
    private readonly IFileStorage _fileStorage;
    private readonly ITicketAssignmentService _assignments;
    private readonly INotifier _notifier;
    private readonly TimeProvider _time;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public UserPrivacyService(
        UserManager<ApplicationUser> userManager, AppDbContext db, ICurrentUserService currentUser, IMemoryCache cache,
        IFileStorage fileStorage, ITicketAssignmentService assignments, INotifier notifier, TimeProvider time)
    {
        _userManager = userManager;
        _db = db;
        _currentUser = currentUser;
        _cache = cache;
        _fileStorage = fileStorage;
        _assignments = assignments;
        _notifier = notifier;
        _time = time;
    }

    public async Task<Outcome<UserExport>> ExportAsync(Guid id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return Outcome<UserExport>.NotFound();

        var company = user.CompanyId is { } companyId ? await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var branch = user.DepartmentId is { } branchId ? await _db.Departments.AsNoTracking().Where(d => d.Id == branchId).Select(d => d.Name).FirstOrDefaultAsync(ct) : null;

        var export = new
        {
            generatedAtUtc = UtcNow,
            profile = new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.PhoneNumber,
                role = user.Role.ToString(),
                company,
                branch,
                user.IsActive,
                user.CreatedAtUtc,
                user.LastLoginAtUtc
            },
            ticketsOpened = await _db.Tickets.AsNoTracking().Where(t => t.ClientId == id).OrderBy(t => t.CreatedAt)
                .Select(t => new { t.TicketNumber, t.Title, description = t.Description, status = t.Status.ToString(), t.CreatedAt, t.ClosedAtUtc }).ToListAsync(ct),
            ticketsAssigned = await (from a in _db.TicketAssignees.AsNoTracking()
                                     join t in _db.Tickets.AsNoTracking() on a.TicketId equals t.Id
                                     where a.UserId == id
                                     orderby t.CreatedAt
                                     select new { t.TicketNumber, t.Title, status = t.Status.ToString(), t.CreatedAt }).ToListAsync(ct),
            messagesWritten = await (from m in _db.TicketMessages.AsNoTracking()
                                     join t in _db.Tickets.AsNoTracking() on m.TicketId equals t.Id
                                     where m.AuthorId == id
                                     orderby m.CreatedAt
                                     select new { t.TicketNumber, type = m.Type.ToString(), m.CreatedAt, m.BodyHtml }).ToListAsync(ct),
            workOnTickets = await (from w in _db.TicketWorkSessions.AsNoTracking()
                                   join t in _db.Tickets.AsNoTracking() on w.TicketId equals t.Id
                                   where w.UserId == id
                                   orderby w.StartedAtUtc
                                   select new { t.TicketNumber, w.StartedAtUtc, w.EndedAtUtc }).ToListAsync(ct),
            workOnTasks = await (from w in _db.WorkTaskSessions.AsNoTracking()
                                 join t in _db.WorkTasks.AsNoTracking() on w.WorkTaskId equals t.Id
                                 where w.UserId == id
                                 orderby w.StartedAtUtc
                                 select new { task = t.Title, w.StartedAtUtc, w.EndedAtUtc }).ToListAsync(ct),
            attachmentsUploaded = await (from a in _db.TicketAttachments.AsNoTracking()
                                         join t in _db.Tickets.AsNoTracking() on a.TicketId equals t.Id
                                         where a.UploadedByUserId == id
                                         orderby a.CreatedAt
                                         select new { t.TicketNumber, a.FileName, a.ContentType, a.FileSizeBytes, a.CreatedAt }).ToListAsync(ct),
            notifications = await _db.Notifications.AsNoTracking().Where(n => n.UserId == id).OrderBy(n => n.CreatedAt)
                .Select(n => new { type = n.Type.ToString(), n.Message, n.IsRead, n.CreatedAt }).ToListAsync(ct),
            actionsRecordedInTheAuditLog = await _db.AuditLogs.AsNoTracking().Where(a => a.UserId == id).OrderBy(a => a.CreatedAt)
                .Select(a => new { a.Action, a.EntityType, a.CreatedAt, a.IpAddress }).ToListAsync(ct),
            // What was done TO this person's account by somebody else (a reset password, an anonymization, an earlier export). Who did it is
            // that person's data, not theirs, so only the action, the time and the note are given.
            actionsRecordedAboutThePerson = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == nameof(ApplicationUser) && a.EntityId == id && a.UserId != id).OrderBy(a => a.CreatedAt)
                .Select(a => new { a.Action, a.CreatedAt, a.Details }).ToListAsync(ct),
            emailsAddressedToThePerson = await ExportOutboxAsync(user.Email, ct)
        };

        // Recorded before the file is handed over, so a request that is given up on before this line neither delivers the data nor leaves a record.
        _db.Audit(_currentUser.UserId, "UserDataExported", nameof(ApplicationUser), user.Id, $"Role of target: {user.Role}");
        await _db.SaveChangesAsync(ct);

        return Outcome<UserExport>.Ok(new UserExport(export, $"user-{user.Id}.json"));
    }

    /// <summary>
    /// The e-mails the system has sent, or tried to send, to this person's address. The address is stored encrypted, so it cannot be
    /// matched in the database and the (short-lived: they are removed after a week or a month) rows are read and compared here. The text of
    /// a message is not part of this: it is removed once the mail is out, and a message that never went out is only listed.
    /// </summary>
    private async Task<List<object>> ExportOutboxAsync(string? address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) return new List<object>();
        var rows = await _db.EmailOutbox.AsNoTracking().OrderBy(o => o.CreatedAt)
            .Select(o => new { o.ToEmail, o.Subject, o.Status, o.Attempts, o.CreatedAt, o.CompletedAtUtc }).ToListAsync(ct);
        return rows
            .Where(o => string.Equals(o.ToEmail, address, StringComparison.OrdinalIgnoreCase))
            .Select(o => (object)new { o.Subject, status = o.Status.ToString(), o.Attempts, o.CreatedAt, o.CompletedAtUtc })
            .ToList();
    }

    public async Task<Outcome<Done>> AnonymizeAsync(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return Outcome<Done>.NotFound();
        if (user.Id == _currentUser.UserId) return Outcome<Done>.Invalid("You cannot anonymize your own account.");
        if (user.UserName?.EndsWith(AnonymizedDomain, StringComparison.OrdinalIgnoreCase) == true) return Outcome<Done>.Ok(Done.Value); // already done

        var avatar = user.ProfilePictureUrl;
        var formerName = $"{user.FirstName} {user.LastName}".Trim();
        var wasEmployee = user.Role == UserRole.Employee;
        var placeholder = $"anonymized-{Guid.NewGuid():N}{AnonymizedDomain}";

        _db.Audit(_currentUser.UserId, "UserAnonymized", nameof(ApplicationUser), user.Id, $"Role of target: {user.Role}");

        user.FirstName = "Obrisan";
        user.LastName = "korisnik";
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.ProfilePictureUrl = null;
        user.IsActive = false;
        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAtUtc = null;
        user.PasswordHash = null;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        // Each of these saves, so the changes above go in with the first one.
        await _userManager.SetUserNameAsync(user, placeholder);
        await _userManager.SetEmailAsync(user, placeholder);
        await _userManager.UpdateSecurityStampAsync(user);
        _cache.Remove(UserStatusCache.Key(user.Id));

        await _db.Notifications.Where(n => n.UserId == id).ExecuteDeleteAsync();
        await _db.SaveChangesAsync();
        await ScrubNameFromGeneratedTextAsync(formerName);
        await _fileStorage.DeletePublicImageAsync(avatar);

        if (wasEmployee)
        {
            await _assignments.ReleaseLiveTicketsAsync(user.Id, "account anonymized", _currentUser.UserId);
            await _db.EndRunningTaskSessionsAsync(null, user.Id, UtcNow); // stop any running task timer, like deactivation does
            var handedBack = await _db.HandBackOpenTasksAsync(_notifier, user.Id, _currentUser.UserId, "account anonymized");
            await _db.SaveChangesAsync();
            foreach (var task in handedBack) await _notifier.DeliverAsync(task);
        }

        return Outcome<Done>.Ok(Done.Value);
    }

    private const string AnonymizedDomain = "@anonymized.invalid";
    private const string AnonymousName = "Obrisan korisnik";

    /// <summary>
    /// Replaces a person's former name in what the application wrote itself: the system events on tickets and the texts of
    /// notifications. Only whole names are replaced ("Ana Bo" is left alone inside "Ana Bošnjak"), and nothing a person typed is touched.
    /// </summary>
    private async Task ScrubNameFromGeneratedTextAsync(string formerName)
    {
        if (formerName.Length < 3) return;

        var variants = new[] { formerName, System.Net.WebUtility.HtmlEncode(formerName) }.Distinct().ToList();
        string Scrub(string text) => variants.Aggregate(text, (current, name) =>
            System.Text.RegularExpressions.Regex.Replace(current, @"(?<!\p{L})" + System.Text.RegularExpressions.Regex.Escape(name) + @"(?!\p{L})", AnonymousName));

        // The database narrows it down; the whole-name rule is applied here, where it can be written as a rule.
        var events = await _db.TicketMessages
            .Where(m => m.Type == MessageType.SystemEvent && variants.Any(v => m.BodyHtml.Contains(v)))
            .ToListAsync();
        foreach (var message in events) message.BodyHtml = Scrub(message.BodyHtml);

        var notifications = await _db.Notifications.Where(n => variants.Any(v => n.Message.Contains(v))).ToListAsync();
        foreach (var notification in notifications) notification.Message = Scrub(notification.Message);

        await _db.SaveChangesAsync();
    }
}
