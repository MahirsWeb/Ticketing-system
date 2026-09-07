namespace TicketSystemTech.Api.Emails;

/// <summary>Plain, dependency-free HTML email templates, branded for Ticket System Tech.</summary>
public static class EmailTemplates
{
    private static string Wrap(string title, string bodyHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var button = buttonText is not null && buttonUrl is not null
            ? $"""
              <tr><td align="center" style="padding:24px 0;text-align:center;">
                <a href="{buttonUrl}" style="background:#2f5ea8;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;font-weight:600;display:inline-block;">{buttonText}</a>
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
        <p>Hi {firstName},</p>
        <p>An account has been created for you on Ticket System Tech. Use the temporary password below to sign in
        — you'll be asked to choose your own password right away.</p>
        <p style="margin:20px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;font-size:13px;">
          <b>Email:</b> {email}<br/>
          <b>Temporary password:</b> <span style="font-family:monospace;font-size:16px;letter-spacing:1px;">{temporaryPassword}</span>
        </p>
        <p style="color:#b91c1c;"><b>This password expires in {validityMinutes} minutes.</b> If it expires before you use it,
        ask whoever invited you to generate a new one.</p>
        """,
        "Sign in", loginUrl);

    public static string VerifyEmail(string firstName, string link) => Wrap(
        "Verify your email address",
        $"<p>Hi {firstName},</p><p>Thanks for setting your password. Please confirm your email address to finish activating your account.</p>",
        "Verify email", link);

    public static string ForgotPassword(string firstName, string link) => Wrap(
        "Reset your password",
        $"<p>Hi {firstName},</p><p>We received a request to reset your password. This link will expire shortly. If you didn't request this, you can safely ignore this email.</p>",
        "Reset password", link);

    public static string TicketOpened(string clientFirstName, string ticketNumber) => Wrap(
        $"Your ticket #{ticketNumber} has been opened",
        $"<p>Hi {clientFirstName},</p><p>Your support ticket <b>#{ticketNumber}</b> has been received and opened. Someone from our team will get back to you as soon as possible.</p>");

    public static string NewTicketSubmitted(string recipientFirstName, string ticketNumber, string ticketTitle, string clientName, string companyName) => Wrap(
        $"New ticket #{ticketNumber} submitted — {companyName}",
        $"""
        <p>Hi {recipientFirstName},</p>
        <p>A new ticket has been submitted to your branch by <b>{clientName}</b> from <b>{companyName}</b>:</p>
        <p style="margin:16px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;">
          <b>#{ticketNumber}</b> — {ticketTitle}
        </p>
        <p>Please review and open it in the ticket system.</p>
        """);

    public static string TicketAssigned(string agentFirstName, string ticketNumber, string ticketTitle) => Wrap(
        $"Ticket #{ticketNumber} assigned to you",
        $"<p>Hi {agentFirstName},</p><p>Ticket <b>#{ticketNumber}</b> — \"{ticketTitle}\" — has been assigned to you. Please review it in the ticket system.</p>");

    public static string TicketClosed(string clientFirstName, string ticketNumber, string resolvedByName) => Wrap(
        $"Your ticket #{ticketNumber} has been resolved",
        $"<p>Hi {clientFirstName},</p><p>Your ticket <b>#{ticketNumber}</b> has been resolved by {resolvedByName}. If you have any further questions, please don't hesitate to reach out.</p>");

    public static string NewResponse(string recipientFirstName, string ticketNumber) => Wrap(
        $"New reply on ticket #{ticketNumber}",
        $"<p>Hi {recipientFirstName},</p><p>There is a new reply on ticket <b>#{ticketNumber}</b>. Please log in to the ticket system to view it.</p>");

    public static string TicketOverdue(string assigneeFirstName, string ticketNumber, string ticketTitle, string reason) => Wrap(
        $"Ticket #{ticketNumber} is overdue",
        $"""
        <p>Hi {assigneeFirstName},</p>
        <p>Ticket <b>#{ticketNumber}</b> — "{ticketTitle}" — is overdue: {reason}.</p>
        <p>Please take a look as soon as you can.</p>
        """);

    public static string BranchWelcome(string branchName, string adminEmail) => Wrap(
        "Welcome to Ticket System Tech",
        $"""
        <p>Hi,</p>
        <p>This mailbox has just been set up as the dedicated email for the <b>{branchName}</b> branch on
        Ticket System Tech. Tickets and internal notifications for this branch will reference this address.</p>
        <p>If you weren't expecting this message, please contact {adminEmail}, the administrator who added it.</p>
        """);

    public static string WorkTaskAssigned(string recipientFirstName, string taskTitle, string assignedByName) => Wrap(
        "A task has been assigned to you",
        $"""
        <p>Hi {recipientFirstName},</p>
        <p>{assignedByName} assigned you a task: <b>"{taskTitle}"</b>.</p>
        <p>Log in to Ticket System Tech to see the details and track your time on it.</p>
        """);

    public static string TicketTransferred(string recipientFirstName, string ticketNumber, string ticketTitle, string fromBranch, string toBranch, string transferredByName) => Wrap(
        $"Ticket #{ticketNumber} transferred to your branch",
        $"""
        <p>Hi {recipientFirstName},</p>
        <p>Ticket <b>#{ticketNumber}</b> — "{ticketTitle}" — has been transferred from <b>{fromBranch}</b> to <b>{toBranch}</b> by {transferredByName}.
        It is now visible to your branch.</p>
        """);
}
