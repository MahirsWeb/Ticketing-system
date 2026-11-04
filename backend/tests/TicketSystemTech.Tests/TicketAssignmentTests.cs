using TicketSystemTech.Domain.Workflow;

namespace TicketSystemTech.Tests;

/// <summary>TS-15.3: who may be put on a ticket is decided in one place, on plain values (no database).</summary>
public class TicketAssignmentTests
{
    private static readonly Guid BranchA = Guid.NewGuid();
    private static readonly Guid BranchB = Guid.NewGuid();
    private static readonly Guid Sub1 = Guid.NewGuid();
    private static readonly Guid Sub2 = Guid.NewGuid();

    [Fact]
    public void An_agent_of_the_ticket_branch_is_accepted_whether_or_not_the_ticket_has_a_sub_branch()
    {
        Assert.Null(TicketAssignment.Check(BranchA, null, BranchA, null));
        Assert.Null(TicketAssignment.Check(BranchA, Sub1, BranchA, null));   // the ticket names no sub-branch, so any agent of the branch will do
        Assert.Null(TicketAssignment.Check(BranchA, Sub1, BranchA, Sub1));
    }

    [Fact]
    public void An_agent_of_another_branch_is_refused()
    {
        Assert.Contains("branch", TicketAssignment.Check(BranchB, null, BranchA, null));
        Assert.Contains("branch", TicketAssignment.Check(BranchB, Sub1, BranchA, Sub1));
    }

    [Fact]
    public void An_agent_with_no_branch_is_refused_for_a_ticket_in_a_branch()
    {
        Assert.NotNull(TicketAssignment.Check(null, null, BranchA, null));
    }

    [Fact]
    public void A_ticket_with_a_sub_branch_needs_an_agent_of_that_sub_branch()
    {
        Assert.Contains("sub-branch", TicketAssignment.Check(BranchA, Sub2, BranchA, Sub1));
        Assert.Contains("sub-branch", TicketAssignment.Check(BranchA, null, BranchA, Sub1));
    }
}
