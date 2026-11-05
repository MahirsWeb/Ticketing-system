using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace TicketSystemTech.Tests;

/// <summary>
/// T-40: a list or a report that the browser has already given up on kept running against the database. Every action that only
/// reads takes the request's cancellation token (or its controller exposes it as <c>Ct</c>), and the analyzer rule CA2016, raised
/// to a warning in <c>backend/.editorconfig</c>, makes the build refuse an action that takes the token and then does not hand it on.
/// Together they leave no way to add a read that cannot be abandoned without a test turning red.
/// </summary>
public class ReadActionsCancellationTests
{
    private static IEnumerable<MethodInfo> ReadActions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpGetAttribute>().Any())
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType)); // a read that never awaits has nothing to cancel

    private static bool HasToken(MethodInfo action)
    {
        if (action.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken))) return true;
        var property = action.DeclaringType!.GetProperty("Ct", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        return property?.PropertyType == typeof(CancellationToken);
    }

    [Fact]
    public void There_are_read_actions_to_check()
    {
        // Guards the test itself: a rename of the attribute or a move of the controllers must not turn the check below into an empty loop.
        Assert.True(ReadActions().Count() >= 30, $"found only {ReadActions().Count()} read actions");
    }

    [Fact]
    public void Every_read_action_can_be_abandoned_by_the_client()
    {
        var without = ReadActions().Where(m => !HasToken(m)).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").OrderBy(n => n).ToList();

        Assert.True(without.Count == 0, "These GET actions do not take the request's CancellationToken: " + string.Join(", ", without));
    }
}
