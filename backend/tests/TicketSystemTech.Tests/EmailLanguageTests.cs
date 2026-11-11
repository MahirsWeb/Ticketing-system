using Microsoft.EntityFrameworkCore;
using TicketSystemTech.Api.Emails;
using TicketSystemTech.Api.Notifications;
using TicketSystemTech.Domain.Entities;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Tests.Integration;

namespace TicketSystemTech.Tests;

/// <summary>
/// TS-47.1: the e-mail templates were English only. They are now written in the language the recipient chose in the application, which is kept on the
/// account (a mail is composed on the server, where the browser's choice is not known). English stays the default and the fall-back.
/// </summary>
public class EmailLanguageTests
{
    private const string Markup = "<img src=x onerror=alert(1)>";

    [Theory]
    [InlineData("bs", "bs")]
    [InlineData("BS", "bs")]
    [InlineData(" bs ", "bs")]
    [InlineData("en", "en")]
    [InlineData("de", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Only_Bosnian_is_Bosnian_everything_else_is_English(string? given, string expected) =>
        Assert.Equal(expected, EmailLanguage.Normalize(given));

    [Fact]
    public void A_template_is_written_in_the_language_asked_for_and_defaults_to_English()
    {
        var english = EmailTemplates.TicketAssigned("Ana", "100001", "Printer");
        var bosnian = EmailTemplates.TicketAssigned("Ana", "100001", "Printer", "bs");

        Assert.Contains("has been assigned to you", english);
        Assert.Contains("lang=\"en\"", english);
        Assert.Contains("dodijeljen je vama", bosnian);
        Assert.Contains("lang=\"bs\"", bosnian);
        Assert.Contains("Ovo je automatska poruka", bosnian);
        Assert.DoesNotContain("has been assigned to you", bosnian);
    }

    [Fact]
    public void Every_template_has_a_Bosnian_version_that_differs_from_the_English_one()
    {
        var pairs = new (string En, string Bs)[]
        {
            (EmailTemplates.Invite("Ana", "a@b.c", "Pw1", 20, "https://x/login"), EmailTemplates.Invite("Ana", "a@b.c", "Pw1", 20, "https://x/login", "bs")),
            (EmailTemplates.AccountLocked("Ana"), EmailTemplates.AccountLocked("Ana", "bs")),
            (EmailTemplates.VerifyEmail("Ana", "https://x/v"), EmailTemplates.VerifyEmail("Ana", "https://x/v", "bs")),
            (EmailTemplates.ForgotPassword("Ana", "https://x/r"), EmailTemplates.ForgotPassword("Ana", "https://x/r", "bs")),
            (EmailTemplates.TicketOpened("Ana", "1"), EmailTemplates.TicketOpened("Ana", "1", "bs")),
            (EmailTemplates.NewTicketSubmitted("Ana", "1", "t", "c", "f"), EmailTemplates.NewTicketSubmitted("Ana", "1", "t", "c", "f", "bs")),
            (EmailTemplates.TicketAssigned("Ana", "1", "t"), EmailTemplates.TicketAssigned("Ana", "1", "t", "bs")),
            (EmailTemplates.TicketClosed("Ana", "1", "Edin"), EmailTemplates.TicketClosed("Ana", "1", "Edin", "bs")),
            (EmailTemplates.NewResponse("Ana", "1"), EmailTemplates.NewResponse("Ana", "1", "bs")),
            (EmailTemplates.TicketOverdue("Ana", "1", "t", "rok"), EmailTemplates.TicketOverdue("Ana", "1", "t", "rok", "bs")),
            (EmailTemplates.BranchWelcome("Mostar", "a@b.c"), EmailTemplates.BranchWelcome("Mostar", "a@b.c", "bs")),
            (EmailTemplates.WorkTaskAssigned("Ana", "t", "Edin"), EmailTemplates.WorkTaskAssigned("Ana", "t", "Edin", "bs")),
            (EmailTemplates.TicketTransferred("Ana", "1", "t", "SA", "MO", "Edin"), EmailTemplates.TicketTransferred("Ana", "1", "t", "SA", "MO", "Edin", "bs")),
        };

        Assert.Equal(13, pairs.Length);
        Assert.All(pairs, p =>
        {
            Assert.NotEqual(p.En, p.Bs);
            Assert.Contains("Ovo je automatska poruka", p.Bs);
            Assert.DoesNotContain("This is an automated message", p.Bs);
        });
    }

    [Fact]
    public void Text_a_user_controls_is_still_encoded_in_the_Bosnian_version()
    {
        var html = EmailTemplates.NewTicketSubmitted("Ana", "100001", Markup, Markup, Markup, "bs");

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img", html);
    }

    private static ApplicationUser Person(string language) => new() { FirstName = "Ana", LastName = "Anić", Email = "ana@x.test", PreferredLanguage = language };

    [Fact]
    public void The_notifier_writes_subject_and_body_in_the_language_of_each_recipient()
    {
        var ticket = new Ticket { TicketNumber = "100001", Title = "Printer" };
        var staff = new[] { Person("bs"), Person("en"), Person("fr") };
        var mails = new Notifier(null!).Emails(new TicketTransferredEvent(ticket, "Sarajevo", "Mostar", Person("en"), staff));

        Assert.Equal(3, mails.Count);
        Assert.Equal("Tiket #100001 je premješten u vašu poslovnicu", mails[0].Subject);
        Assert.Contains("Sada je vidljiv vašoj poslovnici", mails[0].HtmlBody);
        Assert.Equal("Ticket #100001 transferred to your branch", mails[1].Subject);
        Assert.Contains("It is now visible to your branch", mails[1].HtmlBody);
        Assert.Equal("Ticket #100001 transferred to your branch", mails[2].Subject); // a language nobody wrote words for: English
    }

    [Fact]
    public void Account_mails_follow_the_language_too()
    {
        Assert.Equal("Promjena lozinke", AccountMessages.PasswordReset("Ana", "https://x/r", "bs").Subject);
        Assert.Equal("Reset your password", AccountMessages.PasswordReset("Ana", "https://x/r").Subject);
        Assert.Contains("Privremena lozinka", AccountMessages.Invite("Ana", "a@b.c", "Pw1", 20, "https://x", "bs").Html);
    }
}

[Collection(ApiCollection.Name)]
public class EmailLanguageApiTests
{
    private readonly ApiWorld _world;

    public EmailLanguageApiTests(ApiWorld world) => _world = world;

    private Task<string> StoredAsync(Guid id) => _world.Db(db => db.Users.Where(u => u.Id == id).Select(u => u.PreferredLanguage).SingleAsync());

    [SqlFact]
    public async Task A_person_can_tell_the_server_their_language_and_anything_else_means_English()
    {
        try
        {
            var first = await _world.Send(_world.EmpA1, HttpMethod.Put, "/api/users/me/language", new { language = "bs" });
            Assert.True(first.Code == 200, first.Text);
            Assert.Equal("bs", await StoredAsync(_world.EmpA1.Id));

            Assert.Equal(200, (await _world.Send(_world.EmpA1, HttpMethod.Put, "/api/users/me/language", new { language = "klingon" })).Code);
            Assert.Equal("en", await StoredAsync(_world.EmpA1.Id));

            Assert.Equal(401, (await _world.Send(null, HttpMethod.Put, "/api/users/me/language", new { language = "bs" })).Code);
        }
        finally
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.EmpA1.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, "en")));
        }
    }

    [SqlFact]
    public async Task A_mail_to_a_person_who_chose_Bosnian_is_written_in_Bosnian()
    {
        var ticket = new Ticket
        {
            TicketNumber = "LG-" + Guid.NewGuid().ToString("N")[..8],
            Title = "štampač",
            Description = "<p>x</p>",
            ClientId = _world.CliX.Id,
            CompanyId = _world.CompanyId,
            Status = Domain.Enums.TicketStatus.InProgress,
            DepartmentId = _world.BranchA,
            Priority = Domain.Enums.TicketPriority.Medium,
            Source = Domain.Enums.TicketSource.TicketSystem
        };
        await _world.Db(async db => { db.Tickets.Add(ticket); return await db.SaveChangesAsync(); });
        try
        {
            await _world.Db(db => db.Users.Where(u => u.Id == _world.CliX.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, "bs")));

            var reply = await _world.Send(_world.EmpA1, HttpMethod.Post, $"/api/tickets/{ticket.Id}/messages", new { type = "Response", bodyHtml = "<p>pogledajte</p>" });
            Assert.Equal(200, reply.Code);

            var mail = await _world.Db(db => db.EmailOutbox.Where(m => m.Subject.Contains(ticket.TicketNumber)).OrderByDescending(m => m.CreatedAt).FirstAsync());
            Assert.Equal($"Novi odgovor na tiketu #{ticket.TicketNumber}", mail.Subject);
        }
        finally
        {
            await _world.Db(async db =>
            {
                await db.Users.Where(u => u.Id == _world.CliX.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PreferredLanguage, "en"));
                await db.EmailOutbox.Where(m => m.Subject.Contains(ticket.TicketNumber)).ExecuteDeleteAsync();
                await db.Notifications.Where(n => n.TicketId == ticket.Id).ExecuteDeleteAsync();
                await db.TicketMessages.Where(m => m.TicketId == ticket.Id).ExecuteDeleteAsync();
                return await db.Tickets.Where(t => t.Id == ticket.Id).ExecuteDeleteAsync();
            });
        }
    }
}
