using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Application.Common;

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

            case DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } }:
                // A unique index refused a second copy: two requests raced to create the same thing (the same person added
                // twice, the same key used twice). The loser is told to look again, not that the server broke.
                _logger.LogInformation(ex, "Duplicate rejected on {Path} (trace {TraceId})", context.Request.Path, traceId);
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                body = new ApiError(
                    "This was already done by someone else (or by an earlier identical request). Reload the page and check.",
                    traceId, Code: "DUPLICATE");
                break;

            case MailboxNotConnectedException:
                // The same answer whichever action needed the mailbox: it is a state of the branch, not a mistake in the request.
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                body = new ApiError(
                    "No mailbox is connected for your branch. Connect one in the email settings first.",
                    traceId, Code: "MAILBOX_NOT_CONNECTED");
                break;

            case ExternalServiceException external:
                // An outside provider is down, slow or refused us: the caller did nothing wrong, and the message was
                // written to be shown. 503 = try again shortly, 502 = the provider rejected the request.
                _logger.LogWarning(external, "{Service} call failed on {Path} (trace {TraceId})", external.Service, context.Request.Path, traceId);
                context.Response.StatusCode = external.Code == ExternalServiceException.MailboxReconnectRequired ? StatusCodes.Status409Conflict
                    : external.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway;
                body = new ApiError(external.Message, traceId, Code: external.Code ?? "EXTERNAL_SERVICE");
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
