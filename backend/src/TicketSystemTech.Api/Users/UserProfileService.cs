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

/// <summary>A person's own details: the picture, the telephone number.</summary>
public interface IUserProfileService
{
    /// <summary>Uploads a profile picture. A user can set their own; Admin can set anyone's.</summary>
    Task<Outcome<object>> UploadPictureAsync(Guid id, IFormFile? picture);

    Task<Outcome<object>> SetMyPhoneAsync(string? phoneNumber);

    Task<Outcome<Done>> SkipPhonePromptAsync();

    /// <summary>Remembers the language the person chose in the application, so that the e-mails to them are written in it.</summary>
    Task<Outcome<Done>> SetMyLanguageAsync(string? language);
}

public sealed class UserProfileService : IUserProfileService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IFileLinkService _links;

    public UserProfileService(UserManager<ApplicationUser> userManager, ICurrentUserService currentUser, IFileStorage fileStorage, IFileLinkService links)
    {
        _userManager = userManager;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _links = links;
    }

    private const long MaxProfilePictureSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    public async Task<Outcome<object>> UploadPictureAsync(Guid id, IFormFile? picture)
    {
        if (_currentUser.UserId != id && _currentUser.Role != UserRole.Admin) return Outcome<object>.Forbidden();
        var file = picture;

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null) return Outcome<object>.NotFound();

        if (file is null || file.Length == 0) return Outcome<object>.Invalid("No file was provided.");
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedImageExtensions.Contains(extension)) return Outcome<object>.Invalid($"File type '{extension}' is not allowed.");
        if (file.Length > MaxProfilePictureSizeBytes) return Outcome<object>.Invalid("Image exceeds the 5 MB limit.");

        await using var stream = file.OpenReadStream();
        var url = await _fileStorage.SavePublicImageAsync(file.FileName, file.ContentType, stream);

        var previousPicture = user.ProfilePictureUrl;
        user.ProfilePictureUrl = url;
        await _userManager.UpdateAsync(user);
        await _fileStorage.DeletePublicImageAsync(previousPicture); // the old picture is no longer referenced by anything
        return Outcome<object>.Ok(new { profilePictureUrl = _links.ToPublicUrl(url) });
    }

    public async Task<Outcome<object>> SetMyPhoneAsync(string? phoneNumber)
    {
        if (_currentUser.UserId is null) return Outcome<object>.Unauthorized();
        var user = await _userManager.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null) return Outcome<object>.Unauthorized();

        user.PhoneNumber = phoneNumber;
        user.PhoneNumberPrompted = true;
        await _userManager.UpdateAsync(user);
        return Outcome<object>.Ok(new { message = "Phone number saved." });
    }

    public async Task<Outcome<Done>> SkipPhonePromptAsync()
    {
        if (_currentUser.UserId is null) return Outcome<Done>.Unauthorized();
        var user = await _userManager.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null) return Outcome<Done>.Unauthorized();

        user.PhoneNumberPrompted = true;
        await _userManager.UpdateAsync(user);
        return Outcome<Done>.Ok(Done.Value);
    }

    public async Task<Outcome<Done>> SetMyLanguageAsync(string? language)
    {
        if (_currentUser.UserId is null) return Outcome<Done>.Unauthorized();
        var chosen = TicketSystemTech.Api.Emails.EmailLanguage.Normalize(language);
        // The screen says it on every load; nothing is written when it is already so.
        var current = await _userManager.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.PreferredLanguage).FirstOrDefaultAsync();
        if (current is null) return Outcome<Done>.Unauthorized();
        if (current != chosen)
            await _userManager.Users.Where(u => u.Id == _currentUser.UserId.Value).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, chosen));
        return Outcome<Done>.Ok(Done.Value);
    }
}
