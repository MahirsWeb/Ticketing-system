namespace TicketSystemTech.Api.Security;

/// <summary>
/// The names of the rate limits, written once. A limit is named on the registration (Program.cs) and on every endpoint it protects, and a
/// name mistyped on an endpoint is not noticed until somebody calls it (the request fails: there is no policy of that name).
/// <see cref="All"/> is what the test that reads every endpoint holds the names to.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Sign-in and the other account endpoints, per network address.</summary>
    public const string Auth = "auth";

    /// <summary>Trading the refresh cookie for a new access token. Far more generous than <see cref="Auth"/>: a whole office behind one
    /// public address renews its tokens at about the same moment, and a refused renewal must not look like a sign-out. The cookie is
    /// a random 256-bit value, so guessing it is not what this limit is for.</summary>
    public const string Refresh = "refresh";

    /// <summary>The AI chat for clients, per person.</summary>
    public const string Ai = "ai";

    /// <summary>The AI endpoints for staff (search, questions, similar tickets, the summary of a period), per person.</summary>
    public const string AiStaff = "ai-staff";

    public static readonly IReadOnlyList<string> All = new[] { Auth, Refresh, Ai, AiStaff };
}
