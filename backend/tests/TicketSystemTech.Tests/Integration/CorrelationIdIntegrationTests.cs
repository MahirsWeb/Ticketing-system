namespace TicketSystemTech.Tests.Integration;

/// <summary>The request reference, end to end: it is on every answer the real pipeline gives, whatever the outcome.</summary>
[Collection(ApiCollection.Name)]
public class CorrelationIdIntegrationTests
{
    private const string Header = "X-Correlation-Id";

    private readonly ApiWorld _world;

    public CorrelationIdIntegrationTests(ApiWorld world) => _world = world;

    [SqlFact]
    public async Task Every_answer_carries_a_reference_whether_it_succeeds_or_is_refused()
    {
        var ok = await _world.Get(null, "/api/health");
        var unauthenticated = await _world.Get(null, "/api/tickets");
        var forbidden = await _world.Get(_world.CliX, "/api/users");
        var missing = await _world.Get(_world.Admin, "/api/no/such/route");

        foreach (var response in new[] { ok, unauthenticated, forbidden, missing })
            Assert.Matches("^[0-9a-f]{12}$", response.Header(Header) ?? "");

        Assert.Equal(200, ok.Code);
        Assert.Equal(401, unauthenticated.Code);
        Assert.Equal(403, forbidden.Code);
        Assert.Equal(404, missing.Code);
        Assert.Equal(4, new[] { ok, unauthenticated, forbidden, missing }.Select(r => r.Header(Header)).Distinct().Count());
    }

    [SqlFact]
    public async Task A_reference_supplied_with_the_request_comes_back_unchanged()
    {
        var response = await _world.Get(_world.Admin, "/api/notifications", new Dictionary<string, string> { [Header] = "support-case-20261019" });

        Assert.Equal("support-case-20261019", response.Header(Header));
    }

    [SqlFact]
    public async Task A_hostile_reference_is_not_echoed_back()
    {
        var response = await _world.Get(_world.Admin, "/api/notifications", new Dictionary<string, string> { [Header] = "x\u0001<script>" });

        Assert.Matches("^[0-9a-f]{12}$", response.Header(Header) ?? "");
    }

    [SqlFact]
    public async Task The_frontend_in_the_browser_is_allowed_to_read_the_reference()
    {
        // Browsers hide response headers from scripts unless the server lists them as exposed.
        var response = await _world.Get(null, "/api/health", new Dictionary<string, string> { ["Origin"] = "http://localhost:5173" });

        Assert.Contains(Header, response.Header("Access-Control-Expose-Headers") ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
