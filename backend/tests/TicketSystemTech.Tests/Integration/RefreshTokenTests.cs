using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Infrastructure.Identity;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// TS-18.1: the session's long-lived half is an httpOnly cookie, rotated on every use. These drive the real chain — sign in,
/// trade the cookie, reuse an old cookie, sign out — against the real tokens table.
/// </summary>
[Collection(ApiCollection.Name)]
public class RefreshTokenTests : IAsyncLifetime
{
    private readonly ApiWorld _world;
    private readonly List<Actor> _people = new();

    public RefreshTokenTests(ApiWorld world) => _world = world;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (!_world.Available) return;
        var ids = _people.Select(p => p.Id).ToList();
        await _world.Db(db => db.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync());
        using var scope = _world.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in ids)
            if (await users.FindByIdAsync(id.ToString()) is { } user) await users.DeleteAsync(user);
    }

    private async Task<Actor> PersonAsync()
    {
        var person = await _world.CreateActorAsync("refresh" + Guid.NewGuid().ToString("N")[..6], UserRole.Employee, _world.BranchA, null);
        _people.Add(person);
        return person;
    }

    /// <summary>The raw value of the refresh cookie the answer set, or null. Parsed with a regex because the cookie's own
    /// Expires attribute contains a comma, so splitting the header on commas would not work.</summary>
    private static string? RefreshCookie(ApiResponse r)
    {
        if (r.Headers is null || !r.Headers.TryGetValue("Set-Cookie", out var raw)) return null;
        var m = Regex.Match(raw, "tst_refresh=([^;,]+)");
        return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
    }

    private Task<ApiResponse> Login(Actor person, string? sendingCookie = null) =>
        _world.Send(null, HttpMethod.Post, "/api/auth/login", new { email = person.Email, password = ApiWorld.Password },
            sendingCookie is null ? null : new Dictionary<string, string> { ["Cookie"] = $"tst_refresh={sendingCookie}" });

    private static string HashOf(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Makes a cookie that was rotated away look as if that happened a while ago, past the few seconds in which a second
    /// tab racing the first is still forgiven.</summary>
    private Task AgeSpentCookie(string raw) =>
        _world.Db(db => db.RefreshTokens.Where(t => t.TokenHash == HashOf(raw))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow.AddMinutes(-5))));

    private Task<ApiResponse> Refresh(string? cookie) =>
        _world.Send(null, HttpMethod.Post, "/api/auth/refresh", null,
            cookie is null ? null : new Dictionary<string, string> { ["Cookie"] = $"tst_refresh={cookie}" });

    [SqlFact]
    public async Task Signing_in_sets_a_refresh_cookie_and_it_can_be_traded_for_a_fresh_access_token()
    {
        var person = await PersonAsync();

        var login = await Login(person);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        var cookie = RefreshCookie(login);
        Assert.NotNull(cookie);

        var refreshed = await Refresh(cookie);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.Contains("accessToken", refreshed.Text); // a usable session came back
        Assert.NotNull(RefreshCookie(refreshed)); // and the cookie was rotated
    }

    [SqlFact]
    public async Task The_cookie_is_httponly_and_not_readable_by_script()
    {
        var person = await PersonAsync();
        var login = await Login(person);
        var setCookie = login.Headers!["Set-Cookie"];
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task A_rotated_cookie_presented_again_is_treated_as_theft_and_kills_the_whole_session()
    {
        var person = await PersonAsync();
        var first = RefreshCookie(await Login(person));

        var second = RefreshCookie(await Refresh(first)); // rotates: `first` is now spent, `second` is live
        Assert.NotNull(second);
        await AgeSpentCookie(first!); // a replay seconds later would be a second tab; minutes later it is a thief

        // Replaying the spent cookie is the theft signal: it is refused AND every live token of the person is revoked,
        // so the token that was legitimately live a moment ago no longer works either.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(first)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(second)).Status);
    }

    [SqlFact]
    public async Task Signing_out_revokes_the_cookie()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        var logout = await _world.Send(null, HttpMethod.Post, "/api/auth/logout", null,
            new Dictionary<string, string> { ["Cookie"] = $"tst_refresh={cookie}" });
        Assert.Equal(HttpStatusCode.OK, logout.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task A_deactivated_account_can_no_longer_refresh()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        await _world.Db(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == person.Id);
            user.IsActive = false;
            return await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task An_absent_or_unknown_cookie_is_just_a_401_not_an_error()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh("deadbeef")).Status);
    }

    private Task<int> LiveCookiesOf(Actor person) =>
        _world.Db(db => db.RefreshTokens.CountAsync(t => t.UserId == person.Id && t.RevokedAtUtc == null));

    [SqlFact]
    public async Task A_spent_cookie_replayed_within_seconds_is_a_second_tab_and_is_given_the_same_successor()
    {
        var person = await PersonAsync();
        var first = RefreshCookie(await Login(person));

        var second = RefreshCookie(await Refresh(first));
        var again = await Refresh(first); // the same cookie again, straight away: two tabs that woke up together
        Assert.Equal(HttpStatusCode.OK, again.Status);

        Assert.Equal(second, RefreshCookie(again)); // the SAME successor, not a second chain of its own
        Assert.Equal(1, await LiveCookiesOf(person));
        Assert.Equal(HttpStatusCode.OK, (await Refresh(second)).Status);
    }

    [SqlFact]
    public async Task However_often_the_spent_cookie_is_replayed_in_the_window_only_one_sign_in_is_alive()
    {
        var person = await PersonAsync();
        var first = RefreshCookie(await Login(person));
        var second = RefreshCookie(await Refresh(first));

        for (var i = 0; i < 5; i++)
            Assert.Equal(second, RefreshCookie(await Refresh(first)));

        Assert.Equal(1, await LiveCookiesOf(person)); // five replays did not make five sessions
    }

    [SqlFact]
    public async Task Requests_that_present_the_same_cookie_at_once_all_get_the_same_successor()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Refresh(cookie)));

        Assert.All(all, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(all.Select(RefreshCookie).Distinct());
        Assert.Equal(1, await LiveCookiesOf(person));
    }

    [SqlFact]
    public async Task Signing_out_closes_the_window_a_spent_cookie_can_not_bring_the_session_back()
    {
        var person = await PersonAsync();
        var first = RefreshCookie(await Login(person));
        var second = RefreshCookie(await Refresh(first));

        var logout = await _world.Send(null, HttpMethod.Post, "/api/auth/logout", null, new Dictionary<string, string> { ["Cookie"] = $"tst_refresh={second}" });
        Assert.Equal(HttpStatusCode.OK, logout.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(first)).Status); // inside the window, but the person has signed out
        Assert.Equal(0, await LiveCookiesOf(person));
    }

    [SqlFact]
    public async Task Signing_out_with_a_cookie_that_was_just_rotated_away_ends_what_replaced_it_too()
    {
        var person = await PersonAsync();
        var first = RefreshCookie(await Login(person));
        var second = RefreshCookie(await Refresh(first)); // a renewal that was under way while the person signed out

        await _world.Send(null, HttpMethod.Post, "/api/auth/logout", null, new Dictionary<string, string> { ["Cookie"] = $"tst_refresh={first}" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(second)).Status);
        Assert.Equal(0, await LiveCookiesOf(person));
    }

    [SqlFact]
    public async Task A_new_sign_in_on_another_device_ends_the_earlier_one_and_is_not_taken_for_a_theft()
    {
        var person = await PersonAsync();
        var laptop = RefreshCookie(await Login(person));
        var phone = RefreshCookie(await Login(person)); // a different device: no cookie of the first is sent with it

        Assert.Equal(1, await LiveCookiesOf(person)); // one sign-in per person: the laptop's was ended by the phone's
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(laptop)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(phone)).Status); // the laptop being refused did not take the phone's session down with it
    }

    [SqlFact]
    public async Task Changing_the_password_ends_a_cookie_issued_before_it()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        // The way a person actually gets a new password: an admin regenerates a temporary one, the person sets their own.
        var regenerated = await _world.Post(_world.Admin, $"/api/users/{person.Id}/regenerate-temp-password");
        Assert.Equal(HttpStatusCode.OK, regenerated.Status);
        var temporary = JsonDocument.Parse(regenerated.Text).RootElement.GetProperty("temporaryPassword").GetString();
        var set = await _world.Post(null, "/api/auth/set-new-password", new
        {
            email = person.Email,
            temporaryPassword = temporary,
            newPassword = "Brand-New-Pass1!",
            confirmNewPassword = "Brand-New-Pass1!"
        });
        Assert.Equal(HttpStatusCode.OK, set.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task A_cookie_made_before_the_stamp_was_recorded_is_refused()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));
        await _world.Db(db => db.RefreshTokens.Where(t => t.TokenHash == HashOf(cookie!))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SecurityStamp, (string?)null)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task A_locked_account_can_not_sign_in_again_but_the_session_it_already_has_goes_on()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));
        await _world.Db(db => db.Users.Where(u => u.Id == person.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10))));

        Assert.Equal(HttpStatusCode.OK, (await Refresh(cookie)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(person)).Status);
    }

    [SqlFact]
    public async Task A_sign_in_ends_at_its_absolute_date_however_often_it_was_renewed()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));
        await _world.Db(db => db.RefreshTokens.Where(t => t.TokenHash == HashOf(cookie!))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SessionStartedAtUtc, DateTime.UtcNow.AddDays(-40))));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task A_renewed_cookie_never_outlives_the_absolute_end_of_the_sign_in()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));
        var started = DateTime.UtcNow.AddDays(-29);
        await _world.Db(db => db.RefreshTokens.Where(t => t.TokenHash == HashOf(cookie!))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SessionStartedAtUtc, started)));

        var next = RefreshCookie(await Refresh(cookie));
        var row = await _world.Db(db => db.RefreshTokens.SingleAsync(t => t.TokenHash == HashOf(next!)));
        Assert.True(row.ExpiresAtUtc <= started.AddDays(30).AddMinutes(1), "the new cookie must end with the sign-in, not 14 days later");
    }

    [SqlFact]
    public async Task A_new_sign_in_revokes_the_cookie_this_browser_still_held()
    {
        var person = await PersonAsync();
        var old = RefreshCookie(await Login(person));

        var second = await Login(person, sendingCookie: old);
        Assert.NotNull(RefreshCookie(second));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(old)).Status);
    }

    [SqlFact]
    public async Task Another_site_can_neither_sign_a_person_out_nor_spend_their_cookie()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));
        var evil = new Dictionary<string, string> { ["Cookie"] = $"tst_refresh={cookie}", ["Origin"] = "http://evil.example" };

        Assert.Equal(HttpStatusCode.Forbidden, (await _world.Send(null, HttpMethod.Post, "/api/auth/logout", null, evil)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _world.Send(null, HttpMethod.Post, "/api/auth/refresh", null, evil)).Status);

        Assert.Equal(HttpStatusCode.OK, (await Refresh(cookie)).Status); // the cookie is untouched
    }

    [SqlFact]
    public async Task Renewing_sessions_has_its_own_far_more_generous_limit_than_signing_in()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));
        // More calls in a minute than the sign-in limit (15) allows: an office renewing at the same time must not be refused.
        for (var i = 0; i < 20; i++)
        {
            var r = await Refresh(cookie);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.Status);
            cookie = RefreshCookie(r) ?? cookie;
        }
    }

    private Task<ApiResponse> LoginWith(Actor person, string password) =>
        _world.Send(null, HttpMethod.Post, "/api/auth/login", new { email = person.Email, password });

    [SqlFact]
    public async Task A_forgotten_old_device_does_not_take_the_new_sign_in_down_with_it_after_a_password_change()
    {
        var person = await PersonAsync();
        var oldLaptop = RefreshCookie(await Login(person));
        var oldPhone = RefreshCookie(await Login(person));

        var regenerated = await _world.Post(_world.Admin, $"/api/users/{person.Id}/regenerate-temp-password");
        var temporary = JsonDocument.Parse(regenerated.Text).RootElement.GetProperty("temporaryPassword").GetString();
        const string fresh = "Brand-New-Pass1!";
        Assert.Equal(HttpStatusCode.OK, (await _world.Post(null, "/api/auth/set-new-password", new { email = person.Email, temporaryPassword = temporary, newPassword = fresh, confirmNewPassword = fresh })).Status);
        var newLaptop = RefreshCookie(await LoginWith(person, fresh));
        Assert.NotNull(newLaptop);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(oldPhone)).Status);  // the forgotten device is refused ...
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(oldLaptop)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(newLaptop)).Status);          // ... and the session signed in after the change is untouched
    }

    [SqlFact]
    public async Task A_deactivated_account_that_is_switched_on_again_does_not_get_an_old_cookie_back()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        Assert.Equal(HttpStatusCode.OK, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{person.Id}/active", new { isActive = false })).Status);
        Assert.Equal(HttpStatusCode.OK, (await _world.Send(_world.Admin, HttpMethod.Patch, $"/api/users/{person.Id}/active", new { isActive = true })).Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status); // it was never shown while the account was off, and still does not work
    }

    [SqlFact]
    public async Task A_password_reset_by_mail_ends_every_sign_in()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        string token;
        using (var scope = _world.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            token = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(person.Id.ToString()))!);
        }
        const string fresh = "Reset-By-Mail-1!";
        var reset = await _world.Post(null, "/api/auth/reset-password", new { userId = person.Id, token, newPassword = fresh, confirmNewPassword = fresh });
        Assert.Equal(HttpStatusCode.OK, reset.Status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task A_new_password_from_the_administrators_command_line_tool_ends_every_sign_in()
    {
        var admin = await _world.CreateActorAsync("cliadmin" + Guid.NewGuid().ToString("N")[..5], UserRole.Admin, null, null);
        _people.Add(admin);
        var cookie = RefreshCookie(await Login(admin));
        Assert.NotNull(cookie);

        using var output = new StringWriter();
        using (var scope = _world.Factory.Services.CreateScope())
            Assert.Equal(0, await TicketSystemTech.Api.Startup.AdminPasswordReset.RunAsync(scope.ServiceProvider, admin.Email, output));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(cookie)).Status);
    }

    [SqlFact]
    public async Task Renewing_sessions_is_not_held_to_the_sign_in_limit_even_when_that_limit_is_tiny()
    {
        // A second copy of the application whose sign-in limit is 2 a minute and whose renewal limit is generous: if renewing shared the sign-in
        // limit, the third call would be refused. (The shared test host has a limit of 1000, which cannot show the difference.)
        var saved = new Dictionary<string, string?>
        {
            ["RateLimits__AuthPerMinute"] = Environment.GetEnvironmentVariable("RateLimits__AuthPerMinute"),
            ["RateLimits__RefreshPerMinute"] = Environment.GetEnvironmentVariable("RateLimits__RefreshPerMinute"),
        };
        Environment.SetEnvironmentVariable("RateLimits__AuthPerMinute", "2");
        Environment.SetEnvironmentVariable("RateLimits__RefreshPerMinute", "200");
        try
        {
            var person = await PersonAsync();
            await using var strict = new ApiFactory();
            using var client = strict.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            async Task<HttpResponseMessage> Post(string url, object? body, string? cookie = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json") };
                if (cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", $"tst_refresh={cookie}");
                return await client.SendAsync(request);
            }

            var login = await Post("/api/auth/login", new { email = person.Email, password = ApiWorld.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookie = Regex.Match(string.Join(",", login.Headers.GetValues("Set-Cookie")), "tst_refresh=([^;,]+)").Groups[1].Value;

            for (var i = 0; i < 8; i++)
            {
                var renewed = await Post("/api/auth/refresh", null, cookie);
                Assert.Equal(HttpStatusCode.OK, renewed.StatusCode); // never "too many requests"
                cookie = Regex.Match(string.Join(",", renewed.Headers.GetValues("Set-Cookie")), "tst_refresh=([^;,]+)").Groups[1].Value;
            }

            // The strict limit really is 2 in this copy (the sign-in above used one): a sign-in endpoint is refused within the next few calls.
            var codes = new List<HttpStatusCode>();
            for (var i = 0; i < 3; i++) codes.Add((await Post("/api/auth/forgot-password", new { email = "a@b.test" })).StatusCode);
            Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        }
        finally
        {
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
        }
    }
}
