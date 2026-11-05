using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Tests;

/// <summary>NOVO-FE-05: what a piece of editor HTML says, not what it is made of.</summary>
public class HtmlTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p> </p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p>&#160;&nbsp; </p><p><br></p>")]
    [InlineData("<p>\u200b</p>")]
    [InlineData("<div><span></span></div>")]
    public void Html_that_shows_nothing_is_blank(string? html) => Assert.True(HtmlText.IsBlank(html));

    [Theory]
    [InlineData("<p>x</p>")]
    [InlineData("<p>&lt;</p>")]
    [InlineData("<p>  Pozdrav  </p>")]
    [InlineData("<ul><li>stavka</li></ul>")]
    [InlineData("<p><img src=\"data:image/png;base64,AAAA\"></p>")]   // a pasted screenshot alone is content
    [InlineData("<IMG SRC=\"data:image/png;base64,AAAA\">")]
    public void Html_that_shows_something_is_not_blank(string html) => Assert.False(HtmlText.IsBlank(html));
}
