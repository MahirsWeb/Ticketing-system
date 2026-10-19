using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Api.Middleware;
using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-35/T-40: lost-update conflicts and unexpected failures had no structured answer.</summary>
public class ApiExceptionMiddlewareTests
{
    private static async Task<(DefaultHttpContext Context, JsonElement Body)> RunAsync(Exception toThrow, string environment = "Production")
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        context.Response.Body = new MemoryStream();

        var middleware = new ApiExceptionMiddleware(
            _ => throw toThrow, NullLogger<ApiExceptionMiddleware>.Instance, new FakeEnvironment(environment));
        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = context.Response.Body.Length == 0 ? default : JsonDocument.Parse(context.Response.Body).RootElement.Clone();
        return (context, body);
    }

    [Fact]
    public async Task A_lost_update_is_a_409_the_screen_can_explain()
    {
        var (context, body) = await RunAsync(new DbUpdateConcurrencyException("stale"));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("CONCURRENT_UPDATE", body.GetProperty("code").GetString());
        Assert.Contains("changed by someone else", body.GetProperty("message").GetString());
        Assert.Equal("trace-123", body.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task An_unexpected_failure_gives_a_reference_and_no_internals_in_production()
    {
        var (context, body) = await RunAsync(new InvalidOperationException("connection string password=hunter2"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("trace-123", body.GetProperty("message").GetString());
        Assert.DoesNotContain("hunter2", body.ToString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("detail").ValueKind);
    }

    [Fact]
    public async Task Development_includes_the_reason_to_help_the_developer()
    {
        var (_, body) = await RunAsync(new InvalidOperationException("boom"), environment: "Development");

        Assert.Equal("boom", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_provider_that_is_down_is_a_503_with_a_message_meant_to_be_shown()
    {
        var inner = new HttpRequestException("socket closed: secret.internal.host:443");
        var (context, body) = await RunAsync(new ExternalServiceException("Microsoft 365", "Microsoft 365 could not be reached right now.", unavailable: true, inner));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("EXTERNAL_SERVICE", body.GetProperty("code").GetString());
        Assert.Equal("Microsoft 365 could not be reached right now.", body.GetProperty("message").GetString());
        Assert.DoesNotContain("secret.internal.host", body.ToString()); // the inner exception never reaches the client
    }

    [Fact]
    public async Task A_provider_that_refused_the_request_is_a_502()
    {
        var (context, _) = await RunAsync(new ExternalServiceException("Microsoft 365", "Microsoft 365 did not accept the request.", unavailable: false));

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_bad_request_keeps_its_own_status_code()
    {
        var (context, _) = await RunAsync(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_client_that_hung_up_is_not_an_error()
    {
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        context.Response.Body = new MemoryStream();
        aborted.Cancel();

        var middleware = new ApiExceptionMiddleware(
            _ => throw new OperationCanceledException(aborted.Token), NullLogger<ApiExceptionMiddleware>.Instance, new FakeEnvironment("Production"));
        await middleware.InvokeAsync(context);

        Assert.Equal(0, context.Response.Body.Length);
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
