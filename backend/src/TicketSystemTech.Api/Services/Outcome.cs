using Microsoft.AspNetCore.Mvc;

namespace TicketSystemTech.Api.Services;

/// <summary>Why a service did not do what it was asked. The controller turns each into the HTTP answer it always had.</summary>
public enum Failure
{
    /// <summary>404, with <c>{ message }</c> when the service says what was not found, otherwise no body.</summary>
    NotFound,

    /// <summary>403.</summary>
    Forbidden,

    /// <summary>401: there is no signed-in person behind the request.</summary>
    Unauthorized,

    /// <summary>400 with <c>{ message }</c>: the request is not acceptable as it is.</summary>
    Invalid,

    /// <summary>Any other status with a body of its own (and perhaps headers): a 429 with Retry-After, a 401 that carries a code.</summary>
    Status,

    /// <summary>409 with <c>{ message, code }</c> (the shape every 409 of the API has): it clashes with the state of things.</summary>
    Conflict
}

/// <summary>
/// What a service returns instead of an HTTP answer, so the rules can be run (and tested) without a web request. A service never
/// knows about status codes; the controller maps the outcome with <see cref="OutcomeExtensions.ToActionResult{T}"/>.
/// </summary>
public sealed record Outcome<T>
{
    private Outcome(T? value, Failure? failure, string? message, object? body = null, int? statusCode = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        Value = value;
        Failure = failure;
        Message = message;
        Body = body;
        StatusCode = statusCode;
        Headers = headers;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public string? Message { get; }

    /// <summary>The whole answer body, for the rare 400 that carries more than a message.</summary>
    public object? Body { get; }

    /// <summary>The status of a <see cref="Services.Failure.Status"/> failure.</summary>
    public int? StatusCode { get; }

    /// <summary>Headers to set on the answer of a <see cref="Services.Failure.Status"/> failure.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public bool Succeeded => Failure is null;

    public static Outcome<T> Ok(T value) => new(value, null, null);

    public static Outcome<T> NotFound(string? message = null) => new(default, Services.Failure.NotFound, message);

    public static Outcome<T> Forbidden() => new(default, Services.Failure.Forbidden, null);

    public static Outcome<T> Unauthorized() => new(default, Services.Failure.Unauthorized, null);

    public static Outcome<T> Invalid(string message) => new(default, Services.Failure.Invalid, message);

    public static Outcome<T> InvalidBody(object body) => new(default, Services.Failure.Invalid, null, body);

    public static Outcome<T> Status(int statusCode, object body, IReadOnlyDictionary<string, string>? headers = null) =>
        new(default, Services.Failure.Status, null, body, statusCode, headers);

    public static Outcome<T> Conflict(string message) => new(default, Services.Failure.Conflict, message);
}

/// <summary>What an action that has nothing to say besides "done" returns.</summary>
public readonly record struct Done
{
    public static Done Value => default;
}

public static class OutcomeExtensions
{
    private static ObjectResult StatusWith(this ControllerBase controller, int statusCode, object body, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is not null)
            foreach (var (name, value) in headers) controller.Response.Headers[name] = value;
        return new ObjectResult(body) { StatusCode = statusCode };
    }

    /// <summary>Success becomes 200 with the value; each failure becomes the answer the API has always given for it.</summary>
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, Outcome<T> outcome) => outcome.Failure switch
    {
        null => controller.Ok(outcome.Value),
        Failure.NotFound => outcome.Message is null ? controller.NotFound() : controller.NotFound(new { message = outcome.Message }),
        Failure.Forbidden => controller.Forbid(),
        Failure.Unauthorized => controller.Unauthorized(),
        Failure.Status => controller.StatusWith(outcome.StatusCode!.Value, outcome.Body!, outcome.Headers),
        Failure.Invalid => controller.BadRequest(outcome.Body ?? new { message = outcome.Message }),
        Failure.Conflict => controller.Conflict(new { message = outcome.Message, code = "CONFLICT" }),
        _ => throw new InvalidOperationException($"Unknown failure {outcome.Failure}")
    };

    /// <summary>The same for an action that is declared as a plain <c>IActionResult</c>: <see cref="Done"/> becomes an empty 200, any other value is returned as it is.</summary>
    public static IActionResult ToResult<T>(this ControllerBase controller, Outcome<T> outcome) =>
        controller.ToResult(outcome, value => value);

    /// <summary>As above, with a choice of what part of the value is returned.</summary>
    public static IActionResult ToResult<T>(this ControllerBase controller, Outcome<T> outcome, Func<T, object?> select) => outcome.Failure switch
    {
        null => outcome.Value is Done ? controller.Ok() : controller.Ok(select(outcome.Value!)),
        Failure.NotFound => outcome.Message is null ? controller.NotFound() : controller.NotFound(new { message = outcome.Message }),
        Failure.Forbidden => controller.Forbid(),
        Failure.Unauthorized => controller.Unauthorized(),
        Failure.Status => controller.StatusWith(outcome.StatusCode!.Value, outcome.Body!, outcome.Headers),
        Failure.Invalid => controller.BadRequest(outcome.Body ?? new { message = outcome.Message }),
        Failure.Conflict => controller.Conflict(new { message = outcome.Message, code = "CONFLICT" }),
        _ => throw new InvalidOperationException($"Unknown failure {outcome.Failure}")
    };
}
