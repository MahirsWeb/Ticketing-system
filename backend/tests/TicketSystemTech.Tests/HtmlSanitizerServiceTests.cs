using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-01: ticket/message/email HTML is rendered raw in the staff UI.</summary>
public class HtmlSanitizerServiceTests
{
    private static string Sanitize(string? html) => new HtmlSanitizerService().Sanitize(html);

    [Theory]
    [InlineData("<script>alert(1)</script><p>hi</p>", "<script")]
    [InlineData("<img src=x onerror=alert(1)>", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript:")]
    [InlineData("<p onclick=\"steal()\">x</p>", "onclick")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>", "<iframe")]
    [InlineData("<svg><script>alert(1)</script></svg>", "<script")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>", "data:text/html")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2Zy8+\">", "data:image/svg")]
    [InlineData("<form action=\"https://evil.example\"><input name=pw></form>", "<form")]
    [InlineData("<form action=\"https://evil.example\"><input name=pw></form>", "<input")]
    [InlineData("<p style=\"background:url(https://evil.example/track.png)\">x</p>", "evil.example")]
    [InlineData("<style>body{background:url(//evil.example/x)}</style><p>y</p>", "<style")]
    public void Removes_active_content(string input, string forbidden)
    {
        var result = Sanitize(input);

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_ordinary_formatting()
    {
        var result = Sanitize("<p>Hello <b>world</b></p><ul><li>one</li></ul><a href=\"https://example.com\">link</a>");

        Assert.Contains("<b>world</b>", result);
        Assert.Contains("<li>one</li>", result);
        Assert.Contains("https://example.com", result);
    }

    [Fact]
    public void Keeps_harmless_inline_styles_from_the_rich_text_editor()
    {
        var result = Sanitize("<p style=\"text-align: center; color: red\">c</p>");

        Assert.Contains("text-align", result);
        Assert.Contains("color", result);
    }

    [Fact]
    public void Keeps_inline_raster_images_pasted_as_data_uris()
    {
        var result = Sanitize("<img src=\"data:image/png;base64,iVBORw0KGgo=\">");

        Assert.Contains("data:image/png;base64,iVBORw0KGgo=", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_gives_empty_string(string? input) => Assert.Equal(string.Empty, Sanitize(input));
}
