using System.Text.RegularExpressions;
using TicketSystemTech.Api.Reports;

namespace TicketSystemTech.Tests;

/// <summary>The documents the server makes name a status as the application does, in the language of the document: a word is changed in one place, the translations of the frontend, and this fails if the documents do not follow.</summary>
public class DocumentWordsTests
{
    private static string Translations()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "backend")) && Directory.Exists(Path.Combine(dir.FullName, "frontend"))))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "frontend", "src", "i18n", "translations.ts"));
    }

    [Theory]
    [InlineData("New")]
    [InlineData("Open")]
    [InlineData("InProgress")]
    [InlineData("Resolved")]
    [InlineData("Closed")]
    public void A_status_in_a_document_has_the_name_the_application_gives_it_in_both_languages(string status)
    {
        // The file holds the English words first and the Bosnian ones after them.
        var found = Regex.Matches(Translations(), $@"^\s*status_{status}:\s*'([^']*)',", RegexOptions.Multiline);
        Assert.Equal(2, found.Count);

        Assert.Equal(found[0].Groups[1].Value, ReportText.For("en")("st_" + status));
        Assert.Equal(found[1].Groups[1].Value, ReportText.For("bs")("st_" + status));
    }
}
