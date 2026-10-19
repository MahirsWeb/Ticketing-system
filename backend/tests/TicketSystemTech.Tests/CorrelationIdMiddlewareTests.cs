using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TicketSystemTech.Api.Middleware;

namespace TicketSystemTech.Tests;

/// <summary>Every request gets a reference, and that reference ends up in the logs, the response and the error answers.</summary>
public class CorrelationIdMiddlewareTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static async Task<(DefaultHttpContext Context, CollectingSink Logs)> RunAsync(string? incoming = null)
    {
        var sink = new CollectingSink();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var context = new DefaultHttpContext();
        if (incoming is not null) context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;

        var middleware = new CorrelationIdMiddleware(_ =>
        {
            logger.Information("something happened while handling the request");
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        logger.Information("written after the request, outside it");
        return (context, sink);
    }

    [Fact]
    public async Task A_request_without_a_reference_gets_a_new_short_one()
    {
        var (context, _) = await RunAsync();

        var id = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.Matches("^[0-9a-f]{12}$", id);
        Assert.Equal(id, context.TraceIdentifier); // error answers quote TraceIdentifier, so they quote the same reference
    }

    [Fact]
    public async Task Two_requests_never_share_a_reference()
    {
        var (first, _) = await RunAsync();
        var (second, _) = await RunAsync();

        Assert.NotEqual(first.TraceIdentifier, second.TraceIdentifier);
    }

    [Fact]
    public async Task A_reference_supplied_by_the_caller_is_kept_so_one_id_can_follow_a_request_across_systems()
    {
        var (context, _) = await RunAsync("proxy-7f3a9c.req_42");

        Assert.Equal("proxy-7f3a9c.req_42", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        Assert.Equal("proxy-7f3a9c.req_42", context.TraceIdentifier);
    }

    [Theory]
    [InlineData("short")]                                        // too short to be unique
    [InlineData("an-id-that-is-far-too-long-to-be-reasonable-an-id-that-is-far-too-long-to-be-reasonable")]
    [InlineData("has spaces in it")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("abcdefgh\r\nSet-Cookie: x=1")]                  // would forge a header / a log line
    [InlineData("abcdefgh\nINF fake log line")]
    public async Task A_supplied_reference_that_is_not_plain_is_replaced_not_trusted(string hostile)
    {
        var (context, _) = await RunAsync(hostile);

        var id = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.NotEqual(hostile, id);
        Assert.Matches("^[0-9a-f]{12}$", id);
    }

    [Fact]
    public async Task Log_lines_written_while_handling_the_request_carry_its_reference_and_later_ones_do_not()
    {
        var (context, logs) = await RunAsync();

        var during = logs.Events[0];
        Assert.Equal($"\"{context.TraceIdentifier}\"", during.Properties[CorrelationIdMiddleware.LogProperty].ToString());

        var after = logs.Events[1];
        Assert.False(after.Properties.ContainsKey(CorrelationIdMiddleware.LogProperty)); // the reference does not leak into unrelated work
    }
}
