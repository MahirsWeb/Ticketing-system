using System.Net;

namespace TicketSystemTech.Api.Emails;

/// <summary>
/// Plain, dependency-free HTML email templates, branded for Ticket System Tech.
/// Every value that can be influenced by a user (names, ticket titles, company/branch names, reasons) goes
/// through <see cref="E"/> before being interpolated: otherwise a ticket titled with markup would arrive in
/// staff inboxes as a phishing email sent from the system's own, trusted sender address.
/// </summary>
public static class EmailTemplates
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <param name="title">Already-safe HTML (constants, or values passed through <see cref="E"/>).</param>
    /// <param name="bodyHtml">Already-safe HTML (constants, or values passed through <see cref="E"/>).</param>
    private static string Wrap(string title, string bodyHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var button = buttonText is not null && buttonUrl is not null
            ? $"""
              <tr><td align="center" style="padding:24px 0;text-align:center;">
                <a href="{E(buttonUrl)}" style="background:#2f5ea8;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;font-weight:600;display:inline-block;">{buttonText}</a>
              </td></tr>
              """
            : "";

        return $"""
        <!doctype html>
        <html>
        <body style="margin:0;padding:0;background:#f2f4f7;font-family:Arial,Helvetica,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f2f4f7;padding:32px 0;">
            <tr><td align="center">
              <table width="480" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;overflow:hidden;">
                <tr><td style="background:#1a2b4c;padding:20px 32px;">
                  <span style="color:#ffffff;font-size:18px;font-weight:700;">Ticket System Tech</span>
                </td></tr>
                <tr><td style="padding:32px;color:#222222;font-size:14px;line-height:1.6;">
                  <h2 style="margin-top:0;color:#1a2b4c;font-size:18px;">{title}</h2>
                  {bodyHtml}
                </td></tr>
                {(button != "" ? $"<tr><td style=\"padding:0 32px 32px;\">{button}</td></tr>" : "")}
                <tr><td style="padding:16px 32px;background:#f8f9fb;color:#888888;font-size:11px;">
                  This is an automated message from Ticket System Tech. Please do not reply directly to this email.
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    public static string Invite(string firstName, string email, string temporaryPassword, int validityMinutes, string loginUrl) => Wrap(
        "You've been invited to Ticket System Tech",
        $"""
        <p>Hi {E(firstName)},</p>
        <p>An account has been created for you on Ticket System Tech. Use the temporary password below to sign in
        — you'll be asked to choose your own password right away.</p>
        <p style="margin:20px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;font-size:13px;">
          <b>Email:</b> {E(email)}<br/>
          <b>Temporary password:</b> <span style="font-family:monospace;font-size:16px;letter-spacing:1px;">{E(temporaryPassword)}</span>
        </p>
        <p style="color:#b91c1c;"><b>This password expires in {validityMinutes} minutes.</b> If it expires before you use it,
        ask whoever invited you to generate a new one.</p>
        """,
        "Sign in", loginUrl);

    public static string AccountLocked(string firstName) => Wrap(
        "Your account was temporarily locked",
        $"<p>Hi {E(firstName)},</p><p>There were several unsuccessful attempts to sign in to your account, so it has been locked for a few minutes as a precaution. If that was you, wait a little and try again. If it was not, you can reset your password right away with &quot;Forgot your password?&quot; on the sign-in page.</p>");

    public static string VerifyEmail(string firstName, string link) => Wrap(
        "Verify your email address",
        $"<p>Hi {E(firstName)},</p><p>Thanks for setting your password. Please confirm your email address to finish activating your account.</p>",
        "Verify email", link);

    public static string ForgotPassword(string firstName, string link) => Wrap(
        "Reset your password",
        $"<p>Hi {E(firstName)},</p><p>We received a request to reset your password. This link will expire shortly. If you didn't request this, you can safely ignore this email.</p>",
        "Reset password", link);

    public static string TicketOpened(string clientFirstName, string ticketNumber) => Wrap(
        $"Your ticket #{E(ticketNumber)} has been opened",
        $"<p>Hi {E(clientFirstName)},</p><p>Your support ticket <b>#{E(ticketNumber)}</b> has been received and opened. Someone from our team will get back to you as soon as possible.</p>");

    public static string NewTicketSubmitted(string recipientFirstName, string ticketNumber, string ticketTitle, string clientName, string companyName) => Wrap(
        $"New ticket #{E(ticketNumber)} submitted — {E(companyName)}",
        $"""
        <p>Hi {E(recipientFirstName)},</p>
        <p>A new ticket has been submitted to your branch by <b>{E(clientName)}</b> from <b>{E(companyName)}</b>:</p>
        <p style="margin:16px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;">
          <b>#{E(ticketNumber)}</b> — {E(ticketTitle)}
        </p>
        <p>Please review and open it in the ticket system.</p>
        """);

    public static string TicketAssigned(string agentFirstName, string ticketNumber, string ticketTitle) => Wrap(
        $"Ticket #{E(ticketNumber)} assigned to you",
        $"<p>Hi {E(agentFirstName)},</p><p>Ticket <b>#{E(ticketNumber)}</b> — \"{E(ticketTitle)}\" — has been assigned to you. Please review it in the ticket system.</p>");

    public static string TicketClosed(string clientFirstName, string ticketNumber, string resolvedByName) => Wrap(
        $"Your ticket #{E(ticketNumber)} has been resolved",
        $"<p>Hi {E(clientFirstName)},</p><p>Your ticket <b>#{E(ticketNumber)}</b> has been resolved by {E(resolvedByName)}. If you have any further questions, please don't hesitate to reach out.</p>");

    public static string NewResponse(string recipientFirstName, string ticketNumber) => Wrap(
        $"New reply on ticket #{E(ticketNumber)}",
        $"<p>Hi {E(recipientFirstName)},</p><p>There is a new reply on ticket <b>#{E(ticketNumber)}</b>. Please log in to the ticket system to view it.</p>");

    public static string TicketOverdue(string assigneeFirstName, string ticketNumber, string ticketTitle, string reason) => Wrap(
        $"Ticket #{E(ticketNumber)} is overdue",
        $"""
        <p>Hi {E(assigneeFirstName)},</p>
        <p>Ticket <b>#{E(ticketNumber)}</b> — "{E(ticketTitle)}" — is overdue: {E(reason)}.</p>
        <p>Please take a look as soon as you can.</p>
        """);

    public static string BranchWelcome(string branchName, string adminEmail) => Wrap(
        "Welcome to Ticket System Tech",
        $"""
        <p>Hi,</p>
        <p>This mailbox has just been set up as the dedicated email for the <b>{E(branchName)}</b> branch on
        Ticket System Tech. Tickets and internal notifications for this branch will reference this address.</p>
        <p>If you weren't expecting this message, please contact {E(adminEmail)}, the administrator who added it.</p>
        """);

    public static string WorkTaskAssigned(string recipientFirstName, string taskTitle, string assignedByName) => Wrap(
        "A task has been assigned to you",
        $"""
        <p>Hi {E(recipientFirstName)},</p>
        <p>{E(assignedByName)} assigned you a task: <b>"{E(taskTitle)}"</b>.</p>
        <p>Log in to Ticket System Tech to see the details and track your time on it.</p>
        """);

    public static string TicketTransferred(string recipientFirstName, string ticketNumber, string ticketTitle, string fromBranch, string toBranch, string transferredByName) => Wrap(
        $"Ticket #{E(ticketNumber)} transferred to your branch",
        $"""
        <p>Hi {E(recipientFirstName)},</p>
        <p>Ticket <b>#{E(ticketNumber)}</b> — "{E(ticketTitle)}" — has been transferred from <b>{E(fromBranch)}</b> to <b>{E(toBranch)}</b> by {E(transferredByName)}.
        It is now visible to your branch.</p>
        """);
}
