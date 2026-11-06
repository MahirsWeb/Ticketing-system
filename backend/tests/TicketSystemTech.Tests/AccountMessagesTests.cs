using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Notifications;

namespace TicketSystemTech.Tests;

/// <summary>
/// T-47: the e-mails about an account, its security and a new branch are written in one place, next to the ones about tickets. These tests pin
/// the subjects the application has always used, and that each message is the template it is named after, so a change to the words is a decision.
/// </summary>
public class AccountMessagesTests
{
    [Fact]
    public void A_locked_account_is_told_so_in_the_template_for_it()
    {
        var mail = AccountMessages.AccountLocked("Ana");

        Assert.Equal("Your account was temporarily locked", mail.Subject);
        Assert.Equal(EmailTemplates.AccountLocked("Ana"), mail.Html);
    }

    [Fact]
    public void A_client_is_asked_to_verify_the_address_with_the_link_that_was_made()
    {
        var mail = AccountMessages.VerifyEmail("Ana", "https://x.test/verify?token=1");

        Assert.Equal("Please verify your email address", mail.Subject);
        Assert.Equal(EmailTemplates.VerifyEmail("Ana", "https://x.test/verify?token=1"), mail.Html);
        Assert.Contains("https://x.test/verify?token=1", mail.Html);
    }

    [Fact]
    public void The_password_reset_carries_its_link()
    {
        var mail = AccountMessages.PasswordReset("Ana", "https://x.test/reset?token=2");

        Assert.Equal("Reset your password", mail.Subject);
        Assert.Equal(EmailTemplates.ForgotPassword("Ana", "https://x.test/reset?token=2"), mail.Html);
        Assert.Contains("https://x.test/reset?token=2", mail.Html);
    }

    [Fact]
    public void An_invitation_and_a_new_temporary_password_are_one_page_under_two_names()
    {
        var invitation = AccountMessages.Invite("Ana", "ana@x.test", "Temp-1234", 30, "https://x.test/login");
        var again = AccountMessages.NewTemporaryPassword("Ana", "ana@x.test", "Temp-1234", 30, "https://x.test/login");

        Assert.Equal("You've been invited to Ticket System Tech", invitation.Subject);
        Assert.Equal("Your new temporary password — Ticket System Tech", again.Subject);
        Assert.Equal(EmailTemplates.Invite("Ana", "ana@x.test", "Temp-1234", 30, "https://x.test/login"), invitation.Html);
        Assert.Equal(invitation.Html, again.Html);
        Assert.Contains("Temp-1234", invitation.Html);
    }

    [Fact]
    public void A_new_branch_is_welcomed_and_told_who_to_ask()
    {
        var mail = AccountMessages.BranchWelcome("Mostar", "admin@x.test");

        Assert.Equal("Welcome to Ticket System Tech", mail.Subject);
        Assert.Equal(EmailTemplates.BranchWelcome("Mostar", "admin@x.test"), mail.Html);
        Assert.Contains("admin@x.test", mail.Html);
    }

    [Fact]
    public void What_a_person_wrote_is_not_taken_for_markup()
    {
        var mail = AccountMessages.AccountLocked("<b>Ana</b>");

        Assert.DoesNotContain("<b>Ana</b>", mail.Html);
        Assert.Contains("&lt;b&gt;Ana&lt;/b&gt;", mail.Html);
    }
}
