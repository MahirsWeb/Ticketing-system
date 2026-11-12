using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TicketSystemTech.Api.Integration;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";

    /// <summary>The header the key travels in. Not <c>Authorization</c>: that one belongs to the people's tokens.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>Which key the request came with; the rate limit counts per key.</summary>
    public const string ClientClaim = "integration_client";
}

/// <summary>
/// Signs in a request by its <c>X-Api-Key</c> header. It is an authentication scheme of its own that only the integration endpoints ask for
/// (<c>[Authorize(AuthenticationSchemes = ...)]</c>): the people's token is not honoured there, and the key is not honoured anywhere else.
/// A key is not a person, so nothing here looks at accounts, roles or branches.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IntegrationKeyStore _keys;

    public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IntegrationKeyStore keys)
        : base(options, logger, encoder) => _keys = keys;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        var client = _keys.Authenticate(presented.ToString());
        if (client is null)
        {
            // The key itself is never written down, not even a wrong one: it may be a right one with a typo.
            Logger.LogWarning("A request to the integration API came with a key that is not valid (from {Address})", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("The API key is not valid."));
        }

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, client), new Claim(ApiKeyDefaults.ClientClaim, client) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = Scheme.Name;
        await Response.WriteAsJsonAsync(new { message = $"A valid API key is required (header {ApiKeyDefaults.HeaderName})." });
    }
}
