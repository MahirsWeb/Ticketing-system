using TicketSystemTech.Application.WorkTime;

namespace TicketSystemTech.Tests;

/// <summary>What the audit log says about a work session: a dispute about hours needs what the time was and what it became.</summary>
public class WorkSessionAuditTextTests
{
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Nine = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_session_that_is_added_or_deleted_is_recorded_as_it_is()
    {
        var text = WorkSessionAuditText.Of(Session, User, Nine, Nine.AddHours(1));

        Assert.Equal($"session {Session} of user {User}: {Nine:O} - {Nine.AddHours(1):O}", text);
        Assert.DoesNotContain("->", text);
    }

    [Fact]
    public void A_session_that_is_changed_keeps_what_it_was_beside_what_it_became()
    {
        var text = WorkSessionAuditText.Changed(Session, User, Nine, Nine.AddHours(1), Nine.AddMinutes(-30), Nine.AddHours(1.25));

        Assert.Equal(
            $"session {Session} of user {User}: {Nine:O} - {Nine.AddHours(1):O} -> {Nine.AddMinutes(-30):O} - {Nine.AddHours(1.25):O}",
            text);
    }

    [Fact]
    public void A_session_that_is_still_running_says_so_on_either_side()
    {
        var text = WorkSessionAuditText.Changed(Session, User, Nine, null, Nine, Nine.AddHours(2));

        Assert.Contains($"{Nine:O} - running ->", text);
        Assert.DoesNotContain("running", text[(text.IndexOf("->", StringComparison.Ordinal) + 2)..]);
    }
}
