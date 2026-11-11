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

    [SqlFact]
    public async Task A_spent_cookie_replayed_within_seconds_is_a_second_tab_not_a_thief()
    {
        var person = await PersonAsync();
        var first = RefreshCookie(await Login(person));

        var second = RefreshCookie(await Refresh(first));
        var third = await Refresh(first); // the same cookie again, straight away: two tabs that woke up together
        Assert.Equal(HttpStatusCode.OK, third.Status);
        var thirdCookie = RefreshCookie(third);
        Assert.NotNull(thirdCookie);
        Assert.NotEqual(second, thirdCookie);

        // Nobody was thrown out: both cookies that came back are live.
        Assert.Equal(HttpStatusCode.OK, (await Refresh(second)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(thirdCookie)).Status);
    }

    [SqlFact]
    public async Task Requests_that_present_the_same_cookie_at_once_all_succeed_and_the_session_survives()
    {
        var person = await PersonAsync();
        var cookie = RefreshCookie(await Login(person));

        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Refresh(cookie)));
        Assert.All(all, r => Assert.Equal(HttpStatusCode.OK, r.Status));

        foreach (var r in all)
            Assert.Equal(HttpStatusCode.OK, (await Refresh(RefreshCookie(r))).Status);
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
}
