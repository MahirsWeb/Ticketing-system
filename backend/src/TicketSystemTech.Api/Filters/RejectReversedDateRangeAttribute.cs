using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TicketSystemTech.Api.Filters;

/// <summary>
/// Answers 400 when an action's <c>from</c> and <c>to</c> query values are both given and <c>from</c> is the later one.
/// Without it a swapped range quietly produced an empty report that looked like "nothing happened in that period".
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RejectReversedDateRangeAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("from", out var from) && from is DateTime start
            && context.ActionArguments.TryGetValue("to", out var to) && to is DateTime end
            && start > end)
        {
            context.Result = new BadRequestObjectResult(new { message = "The start date must not be after the end date." });
        }
    }
}
