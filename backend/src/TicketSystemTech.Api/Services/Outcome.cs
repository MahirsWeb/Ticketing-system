using Microsoft.AspNetCore.Mvc;

namespace TicketSystemTech.Api.Services;

/// <summary>Why a service did not do what it was asked. The controller turns each into the HTTP answer it always had.</summary>
public enum Failure
{
    /// <summary>404, with <c>{ message }</c> when the service says what was not found, otherwise no body.</summary>
    NotFound,

    /// <summary>403.</summary>
    Forbidden,

    /// <summary>400 with <c>{ message }</c>: the request is not acceptable as it is.</summary>
    Invalid,

    /// <summary>409 with <c>{ message, code }</c> (the shape every 409 of the API has): it clashes with the state of things.</summary>
    Conflict
}

/// <summary>
/// What a service returns instead of an HTTP answer, so the rules can be run (and tested) without a web request. A service never
/// knows about status codes; the controller maps the outcome with <see cref="OutcomeExtensions.ToActionResult{T}"/>.
/// </summary>
public sealed record Outcome<T>
{
    private Outcome(T? value, Failure? failure, string? message)
    {
        Value = value;
        Failure = failure;
        Message = message;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public string? Message { get; }

    public bool Succeeded => Failure is null;

    public static Outcome<T> Ok(T value) => new(value, null, null);

    public static Outcome<T> NotFound(string? message = null) => new(default, Services.Failure.NotFound, message);

    public static Outcome<T> Forbidden() => new(default, Services.Failure.Forbidden, null);

    public static Outcome<T> Invalid(string message) => new(default, Services.Failure.Invalid, message);

    public static Outcome<T> Conflict(string message) => new(default, Services.Failure.Conflict, message);
}

public static class OutcomeExtensions
{
    /// <summary>Success becomes 200 with the value; each failure becomes the answer the API has always given for it.</summary>
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, Outcome<T> outcome) => outcome.Failure switch
    {
        null => controller.Ok(outcome.Value),
        Failure.NotFound => outcome.Message is null ? controller.NotFound() : controller.NotFound(new { message = outcome.Message }),
        Failure.Forbidden => controller.Forbid(),
        Failure.Invalid => controller.BadRequest(new { message = outcome.Message }),
        Failure.Conflict => controller.Conflict(new { message = outcome.Message, code = "CONFLICT" }),
        _ => throw new InvalidOperationException($"Unknown failure {outcome.Failure}")
    };
}
