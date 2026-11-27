using Microsoft.AspNetCore.Identity;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests;

/// <summary>The hash of a password another system made with bcrypt is still recognised, and replaced by Identity's own by the sign-in that proves it.</summary>
public class LegacyPasswordHasherTests
{
    private static readonly LegacyPasswordHasher Hasher = new();
    private static readonly ApplicationUser Person = new();

    // The example in the PHP manual of password_hash / password_verify: a hash made by PHP (the system the old passwords come from), of this password.
    private const string PhpHash = "$2y$07$BCryptRequires22Chrcte/VlQH0piJtjXl.0t1XkA8pw9dMXTpOq";
    private const string PhpPassword = "rasmuslerdorf";

    private static string Bcrypt(string password, string prefix = "$2y$") => BCrypt.Net.BCrypt.HashPassword(password, 4).Replace("$2a$", prefix);

    [Fact]
    public void A_hash_that_PHP_made_accepts_its_password_and_asks_to_be_replaced()
    {
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, Hasher.VerifyHashedPassword(Person, PhpHash, PhpPassword));
    }

    [Fact]
    public void The_classic_bcrypt_test_vector_of_the_a_spelling_is_accepted()
    {
        // From the original test vectors of bcrypt: osTicket's hashes are of this form ($2a$, 60 characters).
        const string hash = "$2a$05$CCCCCCCCCCCCCCCCCCCCC.E5YPO9kmyuRGyh0XouQYb4YMJKvyOeW";

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, Hasher.VerifyHashedPassword(Person, hash, "U*U"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, hash, "U*V"));
    }

    [Fact]
    public void A_wrong_password_a_different_case_and_an_empty_one_are_refused()
    {
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, PhpHash, "rasmuslerdorg"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, PhpHash, "RASMUSLERDORF"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, PhpHash, ""));
    }

    [Theory]
    [InlineData("$2y$")]
    [InlineData("$2a$")]
    [InlineData("$2b$")]
    public void All_three_spellings_of_bcrypt_are_recognised(string prefix)
    {
        var hash = Bcrypt("Stara-lozinka-1", prefix);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, Hasher.VerifyHashedPassword(Person, hash, "Stara-lozinka-1"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, hash, "Stara-lozinka-2"));
    }

    [Fact]
    public void A_password_with_letters_of_the_language_is_read_as_PHP_read_it()
    {
        var hash = Bcrypt("šđčćž-Lozinka-1");

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, Hasher.VerifyHashedPassword(Person, hash, "šđčćž-Lozinka-1"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, hash, "sdccz-Lozinka-1"));
    }

    [Fact]
    public void A_new_password_is_hashed_the_way_Identity_does_it_and_never_with_bcrypt()
    {
        var hash = Hasher.HashPassword(Person, "Nova-lozinka-1");

        Assert.False(LegacyPasswordHasher.IsBcrypt(hash));
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<ApplicationUser>().VerifyHashedPassword(Person, hash, "Nova-lozinka-1"));
        Assert.Equal(PasswordVerificationResult.Success, Hasher.VerifyHashedPassword(Person, hash, "Nova-lozinka-1"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, hash, "Nova-lozinka-2"));
    }

    [Fact]
    public void A_hash_that_Identity_made_before_is_still_accepted_as_it_always_was()
    {
        var identity = new PasswordHasher<ApplicationUser>().HashPassword(Person, "Ranije-1");

        Assert.Equal(PasswordVerificationResult.Success, Hasher.VerifyHashedPassword(Person, identity, "Ranije-1"));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, identity, "Ranije-2"));
    }

    [Theory]
    [InlineData("$2y$10$xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 60 characters that look like bcrypt and are not
    [InlineData("$2y$99$BCryptRequires22Chrcte/VlQH0piJtjXl.0t1XkA8pw9dMXTpOq")] // a cost that does not exist
    [InlineData("$2y$07$BCryptRequires22Chrcte/VlQH0piJtjXl.0t1XkA8pw9dMXTpO!")] // a character that is not of the alphabet
    public void Something_that_only_looks_like_bcrypt_is_a_wrong_password_and_not_an_error(string hash)
    {
        Assert.True(LegacyPasswordHasher.IsBcrypt(hash));

        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(Person, hash, "bilo-sta"));
    }

    [Theory]
    [InlineData("$2y$07$BCryptRequires22Chrcte/VlQH0piJtjXl.0t1XkA8pw9dMXTpOq", true)]
    [InlineData("$2a$10$BCryptRequires22Chrcte/VlQH0piJtjXl.0t1XkA8pw9dMXTpOq", true)]
    [InlineData("$2x$07$BCryptRequires22Chrcte/VlQH0piJtjXl.0t1XkA8pw9dMXTpOq", false)] // the broken spelling of old PHP is not accepted
    [InlineData("$1$salt$abcdefghijklmnopqrstuv", false)] // md5 crypt
    [InlineData("AQAAAAIAAYagAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==", false)] // an Identity hash
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_text_form_of_bcrypt_is_recognised(string? hash, bool expected)
    {
        Assert.Equal(expected, LegacyPasswordHasher.IsBcrypt(hash));
    }
}
