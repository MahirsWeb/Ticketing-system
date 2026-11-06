using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketSystemTech.Api.Security;

namespace TicketSystemTech.Tests;

/// <summary>
/// A rate limit is named where it is registered and on each endpoint it protects. A name that is mistyped on an endpoint is not an error
/// until somebody calls the endpoint, which then fails with "no policy of that name", so every name in use is held here to the ones that exist.
/// </summary>
public class RateLimitPolicyTests
{
    private static IEnumerable<(string Where, string Policy)> NamesInUse() =>
        typeof(RateLimitPolicies).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(controller =>
            {
                var onController = controller.GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true).Select(a => ($"{controller.Name}", a.PolicyName));
                var onActions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<EnableRateLimitingAttribute>(inherit: true).Select(a => ($"{controller.Name}.{m.Name}", a.PolicyName)));
                return onController.Concat(onActions);
            })
            .Where(x => x.Item2 is not null)
            .Select(x => (x.Item1, x.Item2!));

    [Fact]
    public void Every_endpoint_that_is_rate_limited_names_a_policy_that_exists()
    {
        var names = NamesInUse().ToList();

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.True(RateLimitPolicies.All.Contains(n.Policy), $"{n.Where} names the rate limit \"{n.Policy}\", which is not registered"));
    }

    [Fact]
    public void Every_policy_there_is_protects_something()
    {
        var used = NamesInUse().Select(n => n.Policy).ToHashSet();

        Assert.All(RateLimitPolicies.All, p => Assert.Contains(p, used));
    }

    [Fact]
    public void The_names_are_the_ones_that_were_in_use_before_they_were_written_once()
    {
        Assert.Equal(new[] { "auth", "ai", "ai-staff" }, RateLimitPolicies.All);
    }
}
