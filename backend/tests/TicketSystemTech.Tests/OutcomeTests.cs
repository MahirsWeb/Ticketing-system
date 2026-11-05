using Microsoft.AspNetCore.Mvc;
using TicketSystemTech.Api.Services;

namespace TicketSystemTech.Tests;

/// <summary>A service answers with an <see cref="Outcome{T}"/> and the controller turns it into the HTTP answer the API has always given.</summary>
public class OutcomeTests
{
    private sealed class AnyController : ControllerBase { }

    private static IActionResult Map(Outcome<string> outcome) => new AnyController().ToActionResult(outcome).Result
        ?? new OkObjectResult(outcome.Value);

    [Fact]
    public void Success_is_200_with_the_value()
    {
        var result = Assert.IsType<OkObjectResult>(Map(Outcome<string>.Ok("x")));

        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void Not_found_is_404_without_a_body()
    {
        var result = Assert.IsType<NotFoundResult>(Map(Outcome<string>.NotFound()));

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void Forbidden_is_403()
    {
        Assert.IsType<ForbidResult>(Map(Outcome<string>.Forbidden()));
    }

    [Fact]
    public void Invalid_is_400_with_the_message()
    {
        var result = Assert.IsType<BadRequestObjectResult>(Map(Outcome<string>.Invalid("not like this")));

        Assert.Equal(400, result.StatusCode);
        Assert.Equal("not like this", result.Value!.GetType().GetProperty("message")!.GetValue(result.Value));
    }

    [Fact]
    public void Conflict_is_409_with_the_message()
    {
        var result = Assert.IsType<ConflictObjectResult>(Map(Outcome<string>.Conflict("changed meanwhile")));

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("changed meanwhile", result.Value!.GetType().GetProperty("message")!.GetValue(result.Value));
        Assert.Equal("CONFLICT", result.Value!.GetType().GetProperty("code")!.GetValue(result.Value)); // the same shape as every other 409
    }

    [Fact]
    public void Only_a_success_counts_as_succeeded()
    {
        Assert.True(Outcome<string>.Ok("x").Succeeded);
        Assert.False(Outcome<string>.NotFound().Succeeded);
        Assert.False(Outcome<string>.Invalid("m").Succeeded);
    }
}
