using TicketSystemTech.Api.Emails;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-25: user-controlled values must never reach an email as live markup.</summary>
public class EmailTemplatesTests
{
    private const string Markup = "<img src=x onerror=alert(1)><a href=\"https://evil.example\">click</a>";

    [Fact]
    public void NewTicketSubmitted_encodes_title_client_and_company()
    {
        var html = EmailTemplates.NewTicketSubmitted("Ana", "100001", Markup, Markup, Markup);

        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<a href=\"https://evil.example\"", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void TicketAssigned_encodes_title()
    {
        var html = EmailTemplates.TicketAssigned("Ana", "100001", Markup);

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Invite_encodes_name_and_email()
    {
        var html = EmailTemplates.Invite(Markup, "a@b.c", "Pw123456xY", 20, "https://app.example/login");

        Assert.DoesNotContain("<img src=x", html);
    }

    [Fact]
    public void Button_url_is_encoded_for_the_href_attribute()
    {
        var html = EmailTemplates.ForgotPassword("Ana", "https://app.example/reset?a=1&b=2\" onmouseover=\"x");

        Assert.Contains("&amp;b=2", html);
        Assert.DoesNotContain("\" onmouseover=\"", html);
    }
}
