using Microsoft.EntityFrameworkCore;

namespace TicketSystemTech.Api.Middleware;

/// <summary>
/// One place that turns an unhandled exception into a JSON answer the frontend can show. Every endpoint already
/// reports its own mistakes as <c>{ message }</c>; an unexpected failure used to come back as an empty 500 (or, in
/// Development, an HTML page), so the screen could only say "something went wrong" with nothing to quote.
///
/// Sits after CORS on purpose: ASP.NET's built-in exception handler clears the response, headers included, which
/// would strip the CORS headers and make the browser report a network error instead of the real one.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client closed the connection; there is nobody to answer and nothing to log as an error.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await WriteAsync(context, ex);
        }
    }

    private async Task WriteAsync(HttpContext context, Exception ex)
    {
        var traceId = context.TraceIdentifier;
        ApiError body;

        switch (ex)
        {
            case DbUpdateConcurrencyException:
                // Someone else saved the same record between this request reading and writing it.
                _logger.LogInformation(ex, "Concurrent update rejected on {Path} (trace {TraceId})", context.Request.Path, traceId);
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                body = new ApiError(
                    "This was changed by someone else while you were working on it. Reload the page and try again.",
                    traceId, Code: "CONCURRENT_UPDATE");
                break;

            case BadHttpRequestException bad:
                context.Response.StatusCode = bad.StatusCode;
                body = new ApiError(bad.Message, traceId);
                break;

            default:
                _logger.LogError(ex, "Unhandled exception on {Method} {Path} (trace {TraceId})", context.Request.Method, context.Request.Path, traceId);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                body = _environment.IsDevelopment()
                    ? new ApiError("Something went wrong on our side.", traceId, Detail: ex.Message)
                    : new ApiError("Something went wrong on our side. Please try again; if it keeps happening, report this reference: " + traceId, traceId);
                break;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(body);
    }

    private sealed record ApiError(string Message, string TraceId, string? Code = null, string? Detail = null);
}
