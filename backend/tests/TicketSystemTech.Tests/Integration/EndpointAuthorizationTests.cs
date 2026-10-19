using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests.Integration;

/// <summary>
/// The "who may call what" matrix for the whole API, built from the routes the application actually registered rather than
/// from a list somebody has to remember to update. A new endpoint that forgets its [Authorize] fails the first test; one that
/// is meant to be public has to be added to <see cref="PublicEndpoints"/> on purpose.
/// </summary>
[Collection(ApiCollection.Name)]
public class EndpointAuthorizationTests
{
    /// <summary>
    /// The intended access policy of the whole API, one line per route: who may call it. It is written out on purpose. The
    /// behavioural tests below derive their expectations from the routes as registered, so loosening an [Authorize] would
    /// loosen what they expect too; this list is what makes that visible. Changing who may call something means changing
    /// this list in the same commit, which is exactly the review it should get.
    /// "public" = no sign-in, "any signed-in person" = every role, otherwise the roles that may call it.
    /// </summary>
    private static readonly string[] Policy =
    {
        "DELETE /api/departments/{id:guid} => Admin",
        "DELETE /api/help-topics/{id:guid} => Admin",
        "DELETE /api/knowledge-base/documents/{id:guid} => Admin",
        "DELETE /api/sla-plans/{id:guid} => Admin",
        "DELETE /api/ticket-categories/{id:guid} => Admin",
        "DELETE /api/tickets/{id:guid}/assignees/{userId:guid} => Admin+Employee",
        "DELETE /api/tickets/{id:guid}/work-sessions/{sessionId:guid} => Admin+Employee",
        "GET / => public",
        "GET /api/companies => Admin+Employee",
        "GET /api/companies/{id:guid} => Admin+Employee",
        "GET /api/departments => any signed-in person",
        "GET /api/departments/{departmentId:guid}/sub-branches => any signed-in person",
        "GET /api/email-integration/config => Employee",
        "GET /api/email-integration/messages => Employee",
        "GET /api/email-integration/messages/{messageId}/prefill => Employee",
        "GET /api/email-integration/status => Employee",
        "GET /api/files/{key} => public",
        "GET /api/health => public",
        "GET /api/help-topics => any signed-in person",
        "GET /api/knowledge-base/documents => Admin+Employee",
        "GET /api/knowledge-base/reindex-status => Admin",
        "GET /api/knowledge-base/search => Admin+Employee",
        "GET /api/knowledge-base/similar-tickets => Admin+Employee",
        "GET /api/notifications => any signed-in person",
        "GET /api/reports/by-branch => Admin",
        "GET /api/reports/export => Admin+Employee",
        "GET /api/reports/leaderboard => Admin+Employee",
        "GET /api/reports/period-comparison => Admin+Employee",
        "GET /api/reports/summary => Admin+Employee",
        "GET /api/reports/timeseries => Admin+Employee",
        "GET /api/reports/top-issues => Admin+Employee",
        "GET /api/settings/overdue-notifications => Admin",
        "GET /api/sla-plans => Admin+Employee",
        "GET /api/ticket-categories => Admin+Employee",
        "GET /api/tickets => any signed-in person",
        "GET /api/tickets/counts => any signed-in person",
        "GET /api/tickets/{id:guid} => any signed-in person",
        "GET /api/users => Admin+Employee",
        "GET /api/users/lookup-by-email => Admin+Employee",
        "GET /api/users/paged => Admin+Employee",
        "GET /api/users/{id:guid} => Admin+Employee",
        "GET /api/work-tasks => Admin+Employee",
        "GET /api/work-tasks/gantt => Admin+Employee",
        "GET /api/work-tasks/gantt/branch => Admin+Employee",
        "GET /api/work-tasks/{id:guid} => Admin+Employee",
        "GET /health/live => public",
        "GET /health/ready => public",
        "GET /hubs/notifications => any signed-in person",
        "GET /hubs/notifications/negotiate => any signed-in person",
        "PATCH /api/users/me/phone => Client",
        "PATCH /api/users/{id:guid}/active => Admin",
        "PATCH /api/work-tasks/{id:guid}/time => Employee",
        "POST /api/auth/forgot-password => public",
        "POST /api/auth/login => public",
        "POST /api/auth/reset-password => public",
        "POST /api/auth/set-new-password => public",
        "POST /api/auth/verify-email => public",
        "POST /api/companies => Admin+Employee",
        "POST /api/departments => Admin",
        "POST /api/departments/{departmentId:guid}/sub-branches => Admin",
        "POST /api/email-integration/connect => Employee",
        "POST /api/email-integration/disconnect => Employee",
        "POST /api/email-integration/messages/{messageId}/complete-ticket => Employee",
        "POST /api/email-integration/messages/{messageId}/mark => Employee",
        "POST /api/help-topics => Admin",
        "POST /api/knowledge-base/ask => Admin+Employee",
        "POST /api/knowledge-base/ask-client => Client",
        "POST /api/knowledge-base/documents => Admin",
        "POST /api/knowledge-base/documents/{id:guid}/reindex => Admin",
        "POST /api/knowledge-base/reindex-all => Admin",
        "POST /api/notifications/read-all => any signed-in person",
        "POST /api/notifications/{id:guid}/read => any signed-in person",
        "POST /api/reports/ai-insights => Admin+Employee",
        "POST /api/sla-plans => Admin",
        "POST /api/ticket-categories => Admin",
        "POST /api/tickets => Client",
        "POST /api/tickets/on-behalf => Admin+Employee",
        "POST /api/tickets/{id:guid}/assignees => Admin+Employee",
        "POST /api/tickets/{id:guid}/close => Admin+Employee",
        "POST /api/tickets/{id:guid}/messages => any signed-in person",
        "POST /api/tickets/{id:guid}/open => Admin+Employee",
        "POST /api/tickets/{id:guid}/reopen => Admin+Employee",
        "POST /api/tickets/{id:guid}/status => Admin+Employee",
        "POST /api/tickets/{id:guid}/transfer-branch => Admin+Employee",
        "POST /api/tickets/{id:guid}/work-sessions => Admin+Employee",
        "POST /api/tickets/{ticketId:guid}/attachments => any signed-in person",
        "POST /api/users/clients => Admin+Employee",
        "POST /api/users/employees => Admin",
        "POST /api/users/me/skip-phone-prompt => Client",
        "POST /api/users/{id:guid}/profile-picture => any signed-in person",
        "POST /api/users/{id:guid}/regenerate-temp-password => Admin+Employee",
        "POST /api/work-tasks => Admin+Employee",
        "POST /api/work-tasks/{id:guid}/reassign => Employee",
        "POST /api/work-tasks/{id:guid}/start => Employee",
        "POST /api/work-tasks/{id:guid}/stop => Employee",
        "PUT /api/companies/{id:guid} => Admin+Employee",
        "PUT /api/departments/{id:guid} => Admin",
        "PUT /api/help-topics/{id:guid} => Admin",
        "PUT /api/settings/overdue-notifications => Admin",
        "PUT /api/sla-plans/{id:guid} => Admin",
        "PUT /api/sub-branches/{id:guid} => Admin",
        "PUT /api/tickets/{id:guid}/work-sessions/{sessionId:guid} => Admin+Employee",
        "PUT /api/ticket-categories/{id:guid} => Admin",
        "PUT /api/users/{id:guid} => Admin"
    };

    private readonly ApiWorld _world;

    public EndpointAuthorizationTests(ApiWorld world) => _world = world;

    private sealed record Route(string Method, string Pattern, string Url, HashSet<UserRole>? AllowedRoles, bool RequiresSignIn)
    {
        public string Name => $"{Method} /{Pattern.TrimStart('/')}";
    }

    private List<Route> Routes()
    {
        var routes = new List<Route>();
        var source = _world.Factory.Services.GetRequiredService<EndpointDataSource>();

        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            // Health checks and the SignalR hub carry no HTTP-method metadata: they answer a plain GET.
            IReadOnlyList<string> methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is { Count: > 0 } declared
                ? declared
                : new[] { "GET" };

            var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var requiresSignIn = !anonymous && authorize.Count > 0;

            // Several [Authorize] attributes must ALL pass (class-level and method-level); inside one, the roles are alternatives.
            HashSet<UserRole>? allowed = null;
            foreach (var data in authorize.Where(a => !string.IsNullOrWhiteSpace(a.Roles)))
            {
                var roles = data.Roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Enum.Parse<UserRole>).ToHashSet();
                allowed = allowed is null ? roles : allowed.Intersect(roles).ToHashSet();
            }

            var pattern = endpoint.RoutePattern.RawText ?? "";
            foreach (var method in methods)
                routes.Add(new Route(method, pattern, UrlFor(endpoint.RoutePattern), requiresSignIn ? allowed : null, requiresSignIn));
        }
        return routes;
    }

    /// <summary>Fills the route's parameters with values of the right shape; the answer will be 404 or 400, which is irrelevant:
    /// authorization is decided before the endpoint looks at any of them.</summary>
    private static string UrlFor(RoutePattern pattern)
    {
        var segments = pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart parameter when parameter.IsOptional => "",
            RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(p => p.Content == "guid") => Guid.NewGuid().ToString(),
            RoutePatternParameterPart => "x",
            _ => ""
        })));
        return "/" + string.Join('/', segments.Where(s => s.Length > 0));
    }

    [SqlFact]
    public void The_API_has_endpoints_and_the_sample_world_has_every_role()
    {
        Assert.True(Routes().Count > 50, "expected the full API surface to be registered");
        Assert.Equal(new[] { UserRole.Admin, UserRole.Employee, UserRole.Client },
            _world.Actors.Values.Select(a => a.Role).Distinct().OrderBy(r => r).ToArray());
    }

    private static string Describe(Route route) =>
        !route.RequiresSignIn ? "public"
        : route.AllowedRoles is null ? "any signed-in person"
        : string.Join("+", route.AllowedRoles.OrderBy(r => r));

    [SqlFact]
    public void Who_may_call_what_is_exactly_the_documented_policy()
    {
        var actual = Routes().Select(r => $"{r.Name} => {Describe(r)}").ToHashSet();
        var expected = Policy.ToHashSet();

        var changedOrNew = actual.Except(expected).OrderBy(l => l, StringComparer.Ordinal).ToList();
        var goneOrChanged = expected.Except(actual).OrderBy(l => l, StringComparer.Ordinal).ToList();

        Assert.True(changedOrNew.Count == 0 && goneOrChanged.Count == 0,
            "The access rules of the API differ from the documented policy in EndpointAuthorizationTests.Policy.\n"
            + "Now in the code but not in the policy (new endpoint, or its rule changed):\n  " + string.Join("\n  ", changedOrNew)
            + "\nIn the policy but no longer in the code (endpoint removed, or its rule changed):\n  " + string.Join("\n  ", goneOrChanged));
    }

    [SqlFact]
    public async Task Every_protected_endpoint_turns_an_anonymous_caller_away_with_401()
    {
        var failures = new List<string>();
        foreach (var route in Routes().Where(r => r.RequiresSignIn))
        {
            var response = await _world.Send(null, new HttpMethod(route.Method), route.Url, route.Method == "GET" ? null : "{}");
            if (response.Code != 401) failures.Add($"{route.Name} -> {response.Code} (expected 401)");
        }

        Assert.True(failures.Count == 0, "Anonymous access was not refused:\n  " + string.Join("\n  ", failures));
    }

    [SqlFact]
    public async Task Every_role_restricted_endpoint_refuses_the_other_roles_with_403()
    {
        var failures = new List<string>();
        var checkedRoutes = 0;

        foreach (var route in Routes().Where(r => r.AllowedRoles is not null))
        {
            foreach (var actor in new[] { _world.Admin, _world.EmpA1, _world.CliX }.Where(a => !route.AllowedRoles!.Contains(a.Role)))
            {
                checkedRoutes++;
                var response = await _world.Send(actor, new HttpMethod(route.Method), route.Url, route.Method == "GET" ? null : "{}");
                if (response.Code != 403) failures.Add($"{route.Name} as {actor.Role} -> {response.Code} (expected 403)");
            }
        }

        Assert.True(checkedRoutes > 50, $"only {checkedRoutes} role refusals were checked; the route discovery is probably broken");
        Assert.True(failures.Count == 0, "A role reached an endpoint that is not meant for it:\n  " + string.Join("\n  ", failures));
    }

    [SqlFact]
    public async Task Garbage_and_tampered_tokens_are_refused()
    {
        var token = _world.EmpA1.Token;
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        Assert.Equal(200, (await _world.Get(_world.EmpA1, "/api/notifications")).Code);
        Assert.Equal(401, (await _world.Get(_world.EmpA1 with { Token = "not-a-jwt" }, "/api/notifications")).Code);
        Assert.Equal(401, (await _world.Get(_world.EmpA1 with { Token = tampered }, "/api/notifications")).Code);
    }

    [SqlFact]
    public async Task A_token_stops_working_the_moment_the_account_is_deactivated()
    {
        var person = await _world.CreateActorAsync("leaver", UserRole.Employee, _world.BranchA, null);
        Assert.Equal(200, (await _world.Get(person, "/api/notifications")).Code);

        await _world.Db(async db =>
        {
            var user = await db.Users.FindAsync(person.Id);
            user!.IsActive = false;
            return await db.SaveChangesAsync();
        });
        _world.ForgetCachedStatus(person);

        Assert.Equal(401, (await _world.Get(person, "/api/notifications")).Code);
    }

    [SqlFact]
    public async Task A_token_stops_working_when_the_person_is_moved_to_another_branch_or_role()
    {
        var moved = await _world.CreateActorAsync("mover", UserRole.Employee, _world.BranchA, null);
        var promoted = await _world.CreateActorAsync("promoted", UserRole.Employee, _world.BranchA, null);
        Assert.Equal(200, (await _world.Get(moved, "/api/tickets")).Code);

        await _world.Db(async db =>
        {
            (await db.Users.FindAsync(moved.Id))!.DepartmentId = _world.BranchB;
            (await db.Users.FindAsync(promoted.Id))!.Role = UserRole.Admin;
            return await db.SaveChangesAsync();
        });
        _world.ForgetCachedStatus(moved);
        _world.ForgetCachedStatus(promoted);

        // The old token still says "branch A" / "Employee"; it must not be honoured as if it still were true.
        Assert.Equal(401, (await _world.Get(moved, "/api/tickets")).Code);
        Assert.Equal(401, (await _world.Get(promoted, "/api/tickets")).Code);
    }

    [SqlFact]
    public async Task A_token_stops_working_after_a_password_change_rotates_the_security_stamp()
    {
        var person = await _world.CreateActorAsync("rotator", UserRole.Employee, _world.BranchA, null);
        Assert.Equal(200, (await _world.Get(person, "/api/notifications")).Code);

        using (var scope = _world.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TicketSystemTech.Infrastructure.Identity.ApplicationUser>>();
            var user = (await users.FindByIdAsync(person.Id.ToString()))!;
            var result = await users.ChangePasswordAsync(user, ApiWorld.Password, "Another-Pass-2026!x");
            Assert.True(result.Succeeded);
        }
        _world.ForgetCachedStatus(person);

        Assert.Equal(401, (await _world.Get(person, "/api/notifications")).Code);
    }
}
