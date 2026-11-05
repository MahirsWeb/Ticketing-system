using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The rules of signing in and of the passwords (T-29, T-15). Most of them were only ever exercised by hand: every one of the
/// mutations behind these tests passed the suite before they were written. Each test makes the accounts it needs and removes them.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthRulesTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New#Pass123";

    private readonly ApiWorld _world;
    private readonly List<Actor> _people = new();

    public AuthRulesTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var person in _people)
            if (await users.FindByIdAsync(person.Id.ToString()) is { } user) await users.DeleteAsync(user);
    }

    private async Task<Actor> PersonAsync(string key, UserRole role = UserRole.Employee)
    {
        var person = await _world.CreateActorAsync(key + Guid.NewGuid().ToString("N")[..6], role,
            role == UserRole.Employee ? _world.BranchA : null, role == UserRole.Client ? _world.CompanyId : null);
        _people.Add(person);
        return person;
    }

    private Task Change(Actor person, Action<ApplicationUser> change) => _world.Db(async db =>
    {
        var user = await db.Users.SingleAsync(u => u.Id == person.Id);
        change(user);
        return await db.SaveChangesAsync();
    });

    private Task<ApiResponse> LoginAsync(string email, string password) => _world.Post(null, "/api/auth/login", new { email, password });

    private Task<ApplicationUser> StateOf(Actor person) => _world.Db(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == person.Id));

    // ======================= signing in =======================

    [SqlFact]
    public async Task Every_way_a_sign_in_can_fail_gets_the_same_answer()
    {
        var wrongPassword = await PersonAsync("authwrong");
        var inactive = await PersonAsync("authoff");
        await Change(inactive, u => u.IsActive = false);
        var locked = await PersonAsync("authlocked");
        await Change(locked, u => u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10));

        var answers = new[]
        {
            await LoginAsync(wrongPassword.Email, "not-the-password"),
            await LoginAsync("nobody-" + Guid.NewGuid().ToString("N")[..6] + "@authz.test", ApiWorld.Password),
            await LoginAsync(inactive.Email, ApiWorld.Password),         // the right password, but the account is switched off
            await LoginAsync(locked.Email, ApiWorld.Password),           // the right password, but the account is locked
        };

        Assert.All(answers, a => Assert.Equal(401, a.Code));
        Assert.Single(answers.Select(a => a.Text).Distinct()); // not even the wording tells them apart
    }

    [SqlFact]
    public async Task A_wrong_password_is_counted_and_the_right_one_clears_the_count()
    {
        var person = await PersonAsync("authcount");

        await LoginAsync(person.Email, "wrong");
        await LoginAsync(person.Email, "wrong");
        Assert.Equal(2, (await StateOf(person)).AccessFailedCount);

        Assert.Equal(200, (await LoginAsync(person.Email, ApiWorld.Password)).Code);
        Assert.Equal(0, (await StateOf(person)).AccessFailedCount);
    }

    [SqlFact]
    public async Task A_temporary_password_signs_in_to_a_password_change_until_it_expires()
    {
        var fresh = await PersonAsync("authtemp");
        await Change(fresh, u => { u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); });
        var expired = await PersonAsync("authexpired");
        await Change(expired, u => { u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1); });

        var ok = await LoginAsync(fresh.Email, ApiWorld.Password);
        Assert.Equal(200, ok.Code);
        Assert.True(ok.Json.GetProperty("requiresPasswordChange").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, ok.Json.GetProperty("accessToken").ValueKind); // no session yet

        var late = await LoginAsync(expired.Email, ApiWorld.Password);
        Assert.Equal(401, late.Code);
        Assert.Equal("TEMP_PASSWORD_EXPIRED", late.Json.GetProperty("code").GetString());
    }

    [SqlFact]
    public async Task A_client_signs_in_only_after_verifying_the_e_mail_address_but_staff_do_not_need_to()
    {
        var client = await PersonAsync("authclient", UserRole.Client);
        await Change(client, u => u.EmailConfirmed = false);
        var staff = await PersonAsync("authstaff");
        await Change(staff, u => u.EmailConfirmed = false);

        var refused = await LoginAsync(client.Email, ApiWorld.Password);
        Assert.Equal(403, refused.Code);
        Assert.Equal("EMAIL_NOT_VERIFIED", refused.Json.GetProperty("code").GetString());

        Assert.Equal(200, (await LoginAsync(staff.Email, ApiWorld.Password)).Code);
    }

    // ======================= the temporary password =======================

    [SqlFact]
    public async Task Setting_a_new_password_needs_the_temporary_one_and_a_wrong_guess_counts_toward_a_lockout()
    {
        var person = await PersonAsync("authnew");
        await Change(person, u => { u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); });

        var guess = await _world.Post(null, "/api/auth/set-new-password", new { email = person.Email, temporaryPassword = "wrong-guess", newPassword = NewPassword, confirmNewPassword = NewPassword });
        Assert.Equal(400, guess.Code);
        Assert.Equal("TEMP_PASSWORD_INVALID", guess.Json.GetProperty("code").GetString());
        Assert.Equal(1, (await StateOf(person)).AccessFailedCount);

        var set = await _world.Post(null, "/api/auth/set-new-password", new { email = person.Email, temporaryPassword = ApiWorld.Password, newPassword = NewPassword, confirmNewPassword = NewPassword });
        Assert.Equal(200, set.Code);
        var after = await StateOf(person);
        Assert.False(after.MustChangePassword);
        Assert.Null(after.TemporaryPasswordExpiresAtUtc);
        Assert.Equal(200, (await LoginAsync(person.Email, NewPassword)).Code);
        Assert.Equal(401, (await LoginAsync(person.Email, ApiWorld.Password)).Code); // the temporary one is gone
    }

    [SqlFact]
    public async Task A_locked_or_expired_or_switched_off_account_cannot_set_a_new_password_and_is_not_told_why()
    {
        var locked = await PersonAsync("authsetlocked");
        await Change(locked, u => { u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10); });
        var late = await PersonAsync("authsetlate");
        await Change(late, u => { u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1); });
        var off = await PersonAsync("authsetoff");
        await Change(off, u => { u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); u.IsActive = false; });
        var settled = await PersonAsync("authsetsettled"); // no temporary password pending

        var answers = new List<ApiResponse>();
        foreach (var who in new[] { locked, late, off, settled })
            answers.Add(await _world.Post(null, "/api/auth/set-new-password", new { email = who.Email, temporaryPassword = ApiWorld.Password, newPassword = NewPassword, confirmNewPassword = NewPassword }));

        Assert.All(answers, a => Assert.Equal(400, a.Code));
        Assert.Single(answers.Select(a => a.Text).Distinct());
        Assert.Equal(200, (await LoginAsync(settled.Email, ApiWorld.Password)).Code); // nothing was changed
    }

    // ======================= forgotten password =======================

    private async Task<string> ResetTokenAsync(Actor person)
    {
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(person.Id.ToString()))!);
    }

    [SqlFact]
    public async Task A_reset_sets_the_password_ends_a_lockout_and_a_pending_temporary_password_and_old_sessions()
    {
        var person = await PersonAsync("authreset");
        await Change(person, u => { u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10); u.AccessFailedCount = 4; u.MustChangePassword = true; u.TemporaryPasswordExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5); });
        _world.ForgetCachedStatus(person);

        // a session that was opened before the reset (the status check caches an account for a short while)
        Assert.Equal(200, (await _world.Get(person, "/api/notifications")).Code);
        var token = await ResetTokenAsync(person);

        var reset = await _world.Post(null, "/api/auth/reset-password", new { userId = person.Id, token, newPassword = NewPassword, confirmNewPassword = NewPassword });
        Assert.Equal(200, reset.Code);

        var after = await StateOf(person);
        Assert.Null(after.LockoutEnd);                    // someone else's wrong guesses cannot keep the owner out
        Assert.Equal(0, after.AccessFailedCount);
        Assert.False(after.MustChangePassword);           // the expired temporary password no longer blocks the new one
        Assert.Null(after.TemporaryPasswordExpiresAtUtc);
        Assert.Equal(200, (await LoginAsync(person.Email, NewPassword)).Code);
        Assert.Equal(401, (await _world.Get(person, "/api/notifications")).Code); // the old session ended at once, not after the cache runs out
    }

    [SqlFact]
    public async Task A_reset_needs_matching_passwords_and_a_valid_link()
    {
        var person = await PersonAsync("authresetbad");
        var token = await ResetTokenAsync(person);

        Assert.Equal(400, (await _world.Post(null, "/api/auth/reset-password", new { userId = person.Id, token, newPassword = NewPassword, confirmNewPassword = NewPassword + "x" })).Code);
        Assert.Equal(400, (await _world.Post(null, "/api/auth/reset-password", new { userId = person.Id, token = "not-a-token", newPassword = NewPassword, confirmNewPassword = NewPassword })).Code);
        Assert.Equal(400, (await _world.Post(null, "/api/auth/reset-password", new { userId = Guid.NewGuid(), token, newPassword = NewPassword, confirmNewPassword = NewPassword })).Code);
        Assert.Equal(200, (await LoginAsync(person.Email, ApiWorld.Password)).Code); // still the old password
    }

    private int ResetMailsTo(string email) => _world.Db(async db =>
        (await db.EmailOutbox.AsNoTracking().Where(m => m.Subject == "Reset your password").ToListAsync()).Count(m => m.ToEmail == email)).GetAwaiter().GetResult();

    [SqlFact]
    public async Task Asking_for_a_reset_always_gets_the_same_answer_but_only_an_active_account_gets_a_link()
    {
        var active = await PersonAsync("authforgot");
        var inactive = await PersonAsync("authforgotoff");
        await Change(inactive, u => u.IsActive = false);

        var answers = new[]
        {
            await _world.Post(null, "/api/auth/forgot-password", new { email = active.Email }),
            await _world.Post(null, "/api/auth/forgot-password", new { email = inactive.Email }),
            await _world.Post(null, "/api/auth/forgot-password", new { email = "nobody-" + Guid.NewGuid().ToString("N")[..6] + "@authz.test" }),
        };
        Assert.All(answers, a => Assert.Equal(200, a.Code));
        Assert.Single(answers.Select(a => a.Text).Distinct());

        for (var wait = 0; wait < 50 && ResetMailsTo(active.Email) == 0; wait++) await Task.Delay(100); // the mail goes through a background queue
        await Task.Delay(300);
        Assert.Equal(1, ResetMailsTo(active.Email));
        Assert.Equal(0, ResetMailsTo(inactive.Email));
    }
}
