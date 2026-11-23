using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests;

/// <summary>
/// The words in the audit trail of a ticket are what the feed of events for another application reads the person and the statuses out of. The
/// rows that are already in databases were written with exactly these words, so they are written out here, letter for letter: a change of
/// wording that would make the existing rows unreadable fails here.
/// </summary>
public class TicketAuditDetailsTests
{
    private static readonly Guid Person = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Branch = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void The_words_written_are_the_words_that_are_already_in_the_database()
    {
        Assert.Equal("Added 11111111-1111-1111-1111-111111111111", TicketAuditDetails.Added(Person));
        Assert.Equal("Removed 11111111-1111-1111-1111-111111111111", TicketAuditDetails.Removed(Person));
        Assert.Equal("Assigned to 11111111-1111-1111-1111-111111111111; branch 22222222-2222-2222-2222-222222222222", TicketAuditDetails.Opened(Person, Branch));
        Assert.Equal("Assigned to 11111111-1111-1111-1111-111111111111; branch ", TicketAuditDetails.Opened(Person, null));
        Assert.Equal("InProgress -> Resolved", TicketAuditDetails.StatusChanged(TicketStatus.InProgress, TicketStatus.Resolved));
    }

    [Fact]
    public void What_was_written_is_read_back()
    {
        Assert.Equal(Person, TicketAuditDetails.AddedUser(TicketAuditDetails.Added(Person)));
        Assert.Equal(Person, TicketAuditDetails.RemovedUser(TicketAuditDetails.Removed(Person)));
        Assert.Equal(Person, TicketAuditDetails.OpenedAssignee(TicketAuditDetails.Opened(Person, Branch)));
        Assert.Equal(Person, TicketAuditDetails.OpenedAssignee(TicketAuditDetails.Opened(Person, null)));
        Assert.Equal((TicketStatus.Open, TicketStatus.Closed), TicketAuditDetails.StatusChange(TicketAuditDetails.StatusChanged(TicketStatus.Open, TicketStatus.Closed)));
    }

    [Fact]
    public void A_row_that_was_written_by_hand_in_the_old_wording_is_read_too()
    {
        Assert.Equal(Person, TicketAuditDetails.AddedUser("Added 11111111-1111-1111-1111-111111111111"));
        Assert.Equal(Person, TicketAuditDetails.OpenedAssignee("Assigned to 11111111-1111-1111-1111-111111111111; branch 22222222-2222-2222-2222-222222222222"));
        Assert.Equal((TicketStatus.InProgress, TicketStatus.Resolved), TicketAuditDetails.StatusChange("InProgress -> Resolved"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Added")]
    [InlineData("Added somebody")]
    [InlineData("Removed 11111111-1111-1111-1111-111111111111")] // right person, wrong kind of row for Added
    [InlineData("added 11111111-1111-1111-1111-111111111111")]
    public void What_is_not_understood_is_nobody_and_not_an_error(string? text)
    {
        Assert.Null(TicketAuditDetails.AddedUser(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gibberish")]
    [InlineData("Open ->")]
    [InlineData("-> Closed")]
    [InlineData("Open -> 7")] // a number is not a status
    [InlineData("Open -> Nonexistent")]
    [InlineData("open -> closed")]
    public void A_status_that_is_not_understood_is_no_change_and_not_an_error(string? text)
    {
        Assert.Null(TicketAuditDetails.StatusChange(text));
    }
}
