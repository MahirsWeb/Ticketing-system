using TicketSystemTech.Api.Emails;

namespace TicketSystemTech.Api.Notifications;

/// <summary>An e-mail that is composed and not yet sent: what it is called and what it says.</summary>
public sealed record EmailMessage(string Subject, string Html);

/// <summary>
/// The e-mails that are not about a ticket or a task: the ones about an account, its security and a new branch. Next to <see cref="Notifier"/>
/// they are the only place that chooses a subject and a template, so no service writes its own words. How and when each is sent stays with
/// the service that sends it: some wait for the send, some are queued so that the time of the answer does not say whether an account exists.
/// </summary>
public static class AccountMessages
{
    /// <summary>A sign-in was refused often enough to lock the account for a while.</summary>
    public static EmailMessage AccountLocked(string firstName) =>
        new("Your account was temporarily locked", EmailTemplates.AccountLocked(firstName));

    /// <summary>A client who set a password is asked to confirm the address.</summary>
    public static EmailMessage VerifyEmail(string firstName, string link) =>
        new("Please verify your email address", EmailTemplates.VerifyEmail(firstName, link));

    /// <summary>The link to choose a new password.</summary>
    public static EmailMessage PasswordReset(string firstName, string link) =>
        new("Reset your password", EmailTemplates.ForgotPassword(firstName, link));

    /// <summary>A new account: how to sign in the first time.</summary>
    public static EmailMessage Invite(string firstName, string email, string temporaryPassword, int validityMinutes, string loginUrl) =>
        new("You've been invited to Ticket System Tech", EmailTemplates.Invite(firstName, email, temporaryPassword, validityMinutes, loginUrl));

    /// <summary>An existing account was given a new temporary password: the same page as the invitation, under another name.</summary>
    public static EmailMessage NewTemporaryPassword(string firstName, string email, string temporaryPassword, int validityMinutes, string loginUrl) =>
        new("Your new temporary password — Ticket System Tech", EmailTemplates.Invite(firstName, email, temporaryPassword, validityMinutes, loginUrl));

    /// <summary>The address of a new branch is told it exists, and who to ask.</summary>
    public static EmailMessage BranchWelcome(string branchName, string adminEmail) =>
        new("Welcome to Ticket System Tech", EmailTemplates.BranchWelcome(branchName, adminEmail));
}
