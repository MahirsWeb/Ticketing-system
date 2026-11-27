using Microsoft.AspNetCore.Identity;

namespace TicketSystemTech.Infrastructure.Identity;

/// <summary>
/// The password hasher of the application: everything is hashed the way ASP.NET Identity does it, except that a hash that was made by another system
/// with bcrypt (<c>$2y$</c>, <c>$2a$</c> or <c>$2b$</c>, which is what PHP's <c>password_hash</c> makes, so what osTicket kept) is still recognised. That is what
/// lets people who had a password in the system that was replaced go on using it: the old hash is carried over as it is, a correct password is accepted
/// once, and by the same sign-in the hash is replaced with Identity's own (<see cref="PasswordVerificationResult.SuccessRehashNeeded"/>), so a bcrypt hash
/// stays in the database only until its owner first signs in. New and changed passwords are never bcrypt.
/// </summary>
public sealed class LegacyPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private readonly PasswordHasher<ApplicationUser> _identity = new();

    public string HashPassword(ApplicationUser user, string password) => _identity.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (!IsBcrypt(hashedPassword)) return _identity.VerifyHashedPassword(user, hashedPassword, providedPassword);

        try
        {
            return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword) ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Failed;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or BCrypt.Net.SaltParseException)
        {
            // A value in the column that only looks like bcrypt is a wrong password, never an error that tells somebody something about the account.
            return PasswordVerificationResult.Failed;
        }
    }

    /// <summary>True for the text form of a bcrypt hash: 60 characters that start with <c>$2a$</c>, <c>$2b$</c> or <c>$2y$</c>. (An Identity hash is base64 of bytes and never does.)</summary>
    public static bool IsBcrypt(string? hash) =>
        hash is { Length: 60 } && hash[0] == '$' && hash[1] == '2' && (hash[2] is 'a' or 'b' or 'y') && hash[3] == '$';
}
