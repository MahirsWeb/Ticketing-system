using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace TicketSystemTech.Api.Middleware;

/// <summary>
/// Gives every request one short reference and carries it everywhere the request leaves a trace: on every log line written
/// while it is handled (the <c>CorrelationId</c> property), in the <c>X-Correlation-Id</c> response header, and as the
/// request's <see cref="HttpContext.TraceIdentifier"/>, which is what error answers quote as their "reference".
/// A person who reports "it failed, reference 3f9a1c07d2b4" can therefore be matched to the exact log lines, however many
/// requests were running at the time.
///
/// A caller (a reverse proxy, a script, a support person's browser) may supply its own reference in the same header so
/// one id follows a request across systems. It is accepted only if it is plain and short: the value ends up in logs and
/// in a response header, so anything with line breaks or odd characters could forge log lines or split the header.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogProperty = "CorrelationId";

    private static readonly Regex Acceptable = new("^[A-Za-z0-9._-]{8,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var id = Acceptable.IsMatch(supplied) ? supplied : NewId();

        context.TraceIdentifier = id;
        context.Response.Headers[HeaderName] = id;

        using (LogContext.PushProperty(LogProperty, id))
            await _next(context);
    }

    /// <summary>12 hex characters: short enough to read out over the phone, 48 bits of randomness (collisions are irrelevant at this scale).</summary>
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
}
