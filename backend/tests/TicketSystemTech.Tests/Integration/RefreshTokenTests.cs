using System.Net;
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

    private Task<ApiResponse> Login(Actor person) =>
        _world.Send(null, HttpMethod.Post, "/api/auth/login", new { email = person.Email, password = ApiWorld.Password });

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
}
