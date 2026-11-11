using TicketSystemTech.Api.Emails;

namespace TicketSystemTech.Api.Notifications;

/// <summary>An e-mail that is composed and not yet sent: what it is called and what it says.</summary>
public sealed record EmailMessage(string Subject, string Html);

/// <summary>
/// The e-mails that are not about a ticket or a task: the ones about an account, its security and a new branch. Next to <see cref="Notifier"/>
/// they are the only place that chooses a subject and a template, so no service writes its own words. How and when each is sent stays with
/// the service that sends it: some wait for the send, some are queued so that the time of the answer does not say whether an account exists.
/// Each is written in the language the person chose in the application (<see cref="EmailLanguage"/>; English when they have not chosen).
/// </summary>
public static class AccountMessages
{
    /// <summary>A sign-in was refused often enough to lock the account for a while.</summary>
    public static EmailMessage AccountLocked(string firstName, string? language = null) =>
        new(EmailLanguage.Pick(language, "Your account was temporarily locked", "Vaš nalog je privremeno zaključan"), EmailTemplates.AccountLocked(firstName, EmailLanguage.Normalize(language)));

    /// <summary>A client who set a password is asked to confirm the address.</summary>
    public static EmailMessage VerifyEmail(string firstName, string link, string? language = null) =>
        new(EmailLanguage.Pick(language, "Please verify your email address", "Molimo potvrdite svoju e-mail adresu"), EmailTemplates.VerifyEmail(firstName, link, EmailLanguage.Normalize(language)));

    /// <summary>The link to choose a new password.</summary>
    public static EmailMessage PasswordReset(string firstName, string link, string? language = null) =>
        new(EmailLanguage.Pick(language, "Reset your password", "Promjena lozinke"), EmailTemplates.ForgotPassword(firstName, link, EmailLanguage.Normalize(language)));

    /// <summary>A new account: how to sign in the first time.</summary>
    public static EmailMessage Invite(string firstName, string email, string temporaryPassword, int validityMinutes, string loginUrl, string? language = null) =>
        new(EmailLanguage.Pick(language, "You've been invited to Ticket System Tech", "Pozvani ste u Ticket System Tech"),
            EmailTemplates.Invite(firstName, email, temporaryPassword, validityMinutes, loginUrl, EmailLanguage.Normalize(language)));

    /// <summary>An existing account was given a new temporary password: the same page as the invitation, under another name.</summary>
    public static EmailMessage NewTemporaryPassword(string firstName, string email, string temporaryPassword, int validityMinutes, string loginUrl, string? language = null) =>
        new(EmailLanguage.Pick(language, "Your new temporary password — Ticket System Tech", "Vaša nova privremena lozinka — Ticket System Tech"),
            EmailTemplates.Invite(firstName, email, temporaryPassword, validityMinutes, loginUrl, EmailLanguage.Normalize(language)));

    /// <summary>The address of a new branch is told it exists, and who to ask. Written in the language of the administrator who added it.</summary>
    public static EmailMessage BranchWelcome(string branchName, string adminEmail, string? language = null) =>
        new(EmailLanguage.Pick(language, "Welcome to Ticket System Tech", "Dobrodošli u Ticket System Tech"), EmailTemplates.BranchWelcome(branchName, adminEmail, EmailLanguage.Normalize(language)));
}
