using System.Net;

namespace TicketSystemTech.Api.Emails;

/// <summary>
/// Plain, dependency-free HTML email templates, branded for Ticket System Tech, in the language of the person who receives them
/// (<see cref="EmailLanguage"/>: English, or Bosnian when that is what they chose in the application).
/// Every value that can be influenced by a user (names, ticket titles, company/branch names, reasons) goes
/// through <see cref="E"/> before being interpolated: otherwise a ticket titled with markup would arrive in
/// staff inboxes as a phishing email sent from the system's own, trusted sender address.
/// </summary>
public static class EmailTemplates
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string P(string language, string english, string bosnian) => EmailLanguage.IsBosnian(language) ? bosnian : english;

    /// <param name="title">Already-safe HTML (constants, or values passed through <see cref="E"/>).</param>
    /// <param name="bodyHtml">Already-safe HTML (constants, or values passed through <see cref="E"/>).</param>
    private static string Wrap(string language, string title, string bodyHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var button = buttonText is not null && buttonUrl is not null
            ? $"""
              <tr><td align="center" style="padding:24px 0;text-align:center;">
                <a href="{E(buttonUrl)}" style="background:#2f5ea8;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;font-weight:600;display:inline-block;">{buttonText}</a>
              </td></tr>
              """
            : "";

        var footer = P(language,
            "This is an automated message from Ticket System Tech. Please do not reply directly to this email.",
            "Ovo je automatska poruka sistema Ticket System Tech. Molimo ne odgovarajte direktno na ovaj e-mail.");

        return $"""
        <!doctype html>
        <html lang="{EmailLanguage.Normalize(language)}">
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
                {(!string.IsNullOrEmpty(button) ? $"<tr><td style=\"padding:0 32px 32px;\">{button}</td></tr>" : "")}
                <tr><td style="padding:16px 32px;background:#f8f9fb;color:#888888;font-size:11px;">
                  {footer}
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    public static string Invite(string firstName, string email, string temporaryPassword, int validityMinutes, string loginUrl, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, "You've been invited to Ticket System Tech", "Pozvani ste u Ticket System Tech"),
        P(language,
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
            $"""
            <p>Zdravo {E(firstName)},</p>
            <p>Za vas je napravljen nalog u sistemu Ticket System Tech. Za prijavu koristite privremenu lozinku ispod
            — odmah ćete biti zamoljeni da izaberete vlastitu lozinku.</p>
            <p style="margin:20px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;font-size:13px;">
              <b>Email:</b> {E(email)}<br/>
              <b>Privremena lozinka:</b> <span style="font-family:monospace;font-size:16px;letter-spacing:1px;">{E(temporaryPassword)}</span>
            </p>
            <p style="color:#b91c1c;"><b>Ova lozinka ističe za {validityMinutes} min.</b> Ako istekne prije nego što je iskoristite,
            zamolite osobu koja vas je pozvala da napravi novu.</p>
            """),
        P(language, "Sign in", "Prijava"), loginUrl);

    public static string AccountLocked(string firstName, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, "Your account was temporarily locked", "Vaš nalog je privremeno zaključan"),
        P(language,
            $"<p>Hi {E(firstName)},</p><p>There were several unsuccessful attempts to sign in to your account, so it has been locked for a few minutes as a precaution. If that was you, wait a little and try again. If it was not, you can reset your password right away with &quot;Forgot your password?&quot; on the sign-in page.</p>",
            $"<p>Zdravo {E(firstName)},</p><p>Bilo je nekoliko neuspješnih pokušaja prijave na vaš nalog, pa je iz predostrožnosti zaključan na nekoliko minuta. Ako ste to bili vi, malo pričekajte i pokušajte ponovo. Ako niste, lozinku možete odmah promijeniti pomoću „Zaboravljena lozinka?“ na stranici za prijavu.</p>"));

    public static string VerifyEmail(string firstName, string link, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, "Verify your email address", "Potvrdite svoju e-mail adresu"),
        P(language,
            $"<p>Hi {E(firstName)},</p><p>Thanks for setting your password. Please confirm your email address to finish activating your account.</p>",
            $"<p>Zdravo {E(firstName)},</p><p>Hvala što ste postavili lozinku. Potvrdite svoju e-mail adresu da završite aktivaciju naloga.</p>"),
        P(language, "Verify email", "Potvrdi e-mail"), link);

    public static string ForgotPassword(string firstName, string link, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, "Reset your password", "Promjena lozinke"),
        P(language,
            $"<p>Hi {E(firstName)},</p><p>We received a request to reset your password. This link will expire shortly. If you didn't request this, you can safely ignore this email.</p>",
            $"<p>Zdravo {E(firstName)},</p><p>Primili smo zahtjev za promjenu vaše lozinke. Ovaj link uskoro ističe. Ako niste vi tražili promjenu, slobodno zanemarite ovaj e-mail.</p>"),
        P(language, "Reset password", "Promijeni lozinku"), link);

    public static string TicketOpened(string clientFirstName, string ticketNumber, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"Your ticket #{E(ticketNumber)} has been opened", $"Vaš tiket #{E(ticketNumber)} je otvoren"),
        P(language,
            $"<p>Hi {E(clientFirstName)},</p><p>Your support ticket <b>#{E(ticketNumber)}</b> has been received and opened. Someone from our team will get back to you as soon as possible.</p>",
            $"<p>Zdravo {E(clientFirstName)},</p><p>Vaš tiket za podršku <b>#{E(ticketNumber)}</b> je zaprimljen i otvoren. Neko iz našeg tima javit će vam se što je prije moguće.</p>"));

    public static string NewTicketSubmitted(string recipientFirstName, string ticketNumber, string ticketTitle, string clientName, string companyName, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"New ticket #{E(ticketNumber)} submitted — {E(companyName)}", $"Novi tiket #{E(ticketNumber)} — {E(companyName)}"),
        P(language,
            $"""
            <p>Hi {E(recipientFirstName)},</p>
            <p>A new ticket has been submitted to your branch by <b>{E(clientName)}</b> from <b>{E(companyName)}</b>:</p>
            <p style="margin:16px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;">
              <b>#{E(ticketNumber)}</b> — {E(ticketTitle)}
            </p>
            <p>Please review and open it in the ticket system.</p>
            """,
            $"""
            <p>Zdravo {E(recipientFirstName)},</p>
            <p>U vašu poslovnicu je stigao novi tiket od <b>{E(clientName)}</b> iz firme <b>{E(companyName)}</b>:</p>
            <p style="margin:16px 0;padding:14px 18px;background:#f2f4f7;border-radius:6px;">
              <b>#{E(ticketNumber)}</b> — {E(ticketTitle)}
            </p>
            <p>Pregledajte ga i otvorite u sistemu za tikete.</p>
            """));

    public static string TicketAssigned(string agentFirstName, string ticketNumber, string ticketTitle, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"Ticket #{E(ticketNumber)} assigned to you", $"Tiket #{E(ticketNumber)} vam je dodijeljen"),
        P(language,
            $"<p>Hi {E(agentFirstName)},</p><p>Ticket <b>#{E(ticketNumber)}</b> — \"{E(ticketTitle)}\" — has been assigned to you. Please review it in the ticket system.</p>",
            $"<p>Zdravo {E(agentFirstName)},</p><p>Tiket <b>#{E(ticketNumber)}</b> — „{E(ticketTitle)}“ — dodijeljen je vama. Pregledajte ga u sistemu za tikete.</p>"));

    public static string TicketClosed(string clientFirstName, string ticketNumber, string resolvedByName, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"Your ticket #{E(ticketNumber)} has been resolved", $"Vaš tiket #{E(ticketNumber)} je riješen"),
        P(language,
            $"<p>Hi {E(clientFirstName)},</p><p>Your ticket <b>#{E(ticketNumber)}</b> has been resolved by {E(resolvedByName)}. If you have any further questions, please don't hesitate to reach out.</p>",
            $"<p>Zdravo {E(clientFirstName)},</p><p>Vaš tiket <b>#{E(ticketNumber)}</b> riješio je/la {E(resolvedByName)}. Ako imate dodatnih pitanja, slobodno nam se javite.</p>"));

    public static string NewResponse(string recipientFirstName, string ticketNumber, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"New reply on ticket #{E(ticketNumber)}", $"Novi odgovor na tiketu #{E(ticketNumber)}"),
        P(language,
            $"<p>Hi {E(recipientFirstName)},</p><p>There is a new reply on ticket <b>#{E(ticketNumber)}</b>. Please log in to the ticket system to view it.</p>",
            $"<p>Zdravo {E(recipientFirstName)},</p><p>Stigao je novi odgovor na tiketu <b>#{E(ticketNumber)}</b>. Prijavite se u sistem za tikete da ga pročitate.</p>"));

    public static string TicketOverdue(string assigneeFirstName, string ticketNumber, string ticketTitle, string reason, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"Ticket #{E(ticketNumber)} is overdue", $"Tiket #{E(ticketNumber)} kasni"),
        P(language,
            $"""
            <p>Hi {E(assigneeFirstName)},</p>
            <p>Ticket <b>#{E(ticketNumber)}</b> — "{E(ticketTitle)}" — is overdue: {E(reason)}.</p>
            <p>Please take a look as soon as you can.</p>
            """,
            $"""
            <p>Zdravo {E(assigneeFirstName)},</p>
            <p>Tiket <b>#{E(ticketNumber)}</b> — „{E(ticketTitle)}“ — kasni: {E(reason)}.</p>
            <p>Pogledajte ga što prije možete.</p>
            """));

    public static string BranchWelcome(string branchName, string adminEmail, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, "Welcome to Ticket System Tech", "Dobrodošli u Ticket System Tech"),
        P(language,
            $"""
            <p>Hi,</p>
            <p>This mailbox has just been set up as the dedicated email for the <b>{E(branchName)}</b> branch on
            Ticket System Tech. Tickets and internal notifications for this branch will reference this address.</p>
            <p>If you weren't expecting this message, please contact {E(adminEmail)}, the administrator who added it.</p>
            """,
            $"""
            <p>Zdravo,</p>
            <p>Ovaj sandučić je upravo podešen kao namjenski e-mail poslovnice <b>{E(branchName)}</b> u sistemu
            Ticket System Tech. Tiketi i interna obavještenja ove poslovnice koristit će ovu adresu.</p>
            <p>Ako niste očekivali ovu poruku, obratite se na {E(adminEmail)}, administratoru koji ju je dodao.</p>
            """));

    public static string WorkTaskAssigned(string recipientFirstName, string taskTitle, string assignedByName, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, "A task has been assigned to you", "Dodijeljen vam je zadatak"),
        P(language,
            $"""
            <p>Hi {E(recipientFirstName)},</p>
            <p>{E(assignedByName)} assigned you a task: <b>"{E(taskTitle)}"</b>.</p>
            <p>Log in to Ticket System Tech to see the details and track your time on it.</p>
            """,
            $"""
            <p>Zdravo {E(recipientFirstName)},</p>
            <p>{E(assignedByName)} vam je dodijelio/la zadatak: <b>„{E(taskTitle)}“</b>.</p>
            <p>Prijavite se u Ticket System Tech da vidite detalje i bilježite vrijeme rada na njemu.</p>
            """));

    public static string TicketTransferred(string recipientFirstName, string ticketNumber, string ticketTitle, string fromBranch, string toBranch, string transferredByName, string language = EmailLanguage.English) => Wrap(
        language,
        P(language, $"Ticket #{E(ticketNumber)} transferred to your branch", $"Tiket #{E(ticketNumber)} je premješten u vašu poslovnicu"),
        P(language,
            $"""
            <p>Hi {E(recipientFirstName)},</p>
            <p>Ticket <b>#{E(ticketNumber)}</b> — "{E(ticketTitle)}" — has been transferred from <b>{E(fromBranch)}</b> to <b>{E(toBranch)}</b> by {E(transferredByName)}.
            It is now visible to your branch.</p>
            """,
            $"""
            <p>Zdravo {E(recipientFirstName)},</p>
            <p>Tiket <b>#{E(ticketNumber)}</b> — „{E(ticketTitle)}“ — premjestio/la je {E(transferredByName)} iz poslovnice <b>{E(fromBranch)}</b> u <b>{E(toBranch)}</b>.
            Sada je vidljiv vašoj poslovnici.</p>
            """));
}
