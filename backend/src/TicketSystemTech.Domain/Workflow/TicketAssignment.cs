namespace TicketSystemTech.Domain.Workflow;

/// <summary>
/// The one place that says who may be put on a ticket. The same three checks (is it a support agent, is the agent in the ticket's
/// branch, is the agent in its sub-branch) used to be written out in three endpoints, and the copies had already started to
/// say different things. Kept free of the database so it can be tested on plain values.
/// </summary>
public static class TicketAssignment
{
    public const string NotAnAgent = "Assignee must be a support agent or consultant.";

    /// <summary>
    /// Returns why the agent cannot work on a ticket in this branch (and sub-branch, when the ticket has one), or null if they can.
    /// Whether the person is an active support agent at all is the caller's question to the database; <see cref="NotAnAgent"/> is the answer.
    /// </summary>
    public static string? Check(Guid? assigneeDepartmentId, Guid? assigneeSubBranchId, Guid? ticketDepartmentId, Guid? ticketSubBranchId)
    {
        if (assigneeDepartmentId != ticketDepartmentId)
            return "Assignee must belong to the ticket's branch.";
        if (ticketSubBranchId.HasValue && assigneeSubBranchId != ticketSubBranchId)
            return "Assignee must belong to the ticket's sub-branch.";
        return null;
    }
}
