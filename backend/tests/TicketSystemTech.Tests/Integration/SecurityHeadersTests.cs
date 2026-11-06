using System.Net;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The baseline response headers (Program.cs) had no test: a future edit could drop one without anything noticing.
/// These pin the hardening headers the API must send on an ordinary JSON answer. The "Server" banner is suppressed
/// at the Kestrel level (AddServerHeader = false), which the in-memory TestServer does not exercise, so that part is
/// verified against the running build in the penetration run, not here.
/// </summary>
[Collection(ApiCollection.Name)]
public class SecurityHeadersTests
{
    private readonly ApiWorld _world;

    public SecurityHeadersTests(ApiWorld world) => _world = world;

    [Fact]
    public async Task Api_answer_carries_the_hardening_headers()
    {
        if (!_world.Available) return;

        var res = await _world.Get(_world.Admin, "/api/tickets?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, res.Status);
        var h = res.Headers!;

        Assert.Equal("nosniff", h["X-Content-Type-Options"]);
        Assert.Equal("DENY", h["X-Frame-Options"]);
        Assert.Equal("no-referrer", h["Referrer-Policy"]);
        Assert.Contains("camera=()", h["Permissions-Policy"]);
        // Testing is not Development, so the API also sends a locked-down CSP on non-file routes.
        Assert.Contains("default-src 'none'", h["Content-Security-Policy"]);
        Assert.Contains("frame-ancestors 'none'", h["Content-Security-Policy"]);
    }

    [Fact]
    public async Task File_route_does_not_send_the_json_csp()
    {
        if (!_world.Available) return;

        // A bogus key answers 404 but still runs the header middleware; the /api/files path is exempt from the CSP
        // (an inline PDF needs the browser's own viewer) yet must keep nosniff.
        var res = await _world.Get(null, "/api/files/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.Status);
        Assert.Equal("nosniff", res.Headers!["X-Content-Type-Options"]);
        Assert.False(res.Headers!.ContainsKey("Content-Security-Policy"));
    }
}
