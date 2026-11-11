namespace TicketSystemTech.Api.Emails;

/// <summary>
/// The language an e-mail is written in: the one the recipient chose in the application (it is kept on their account, because a mail is composed on the
/// server, where the browser's choice is not known). English is the default and the fall-back for anything that is not a language this has words for.
/// </summary>
public static class EmailLanguage
{
    public const string English = "en";
    public const string Bosnian = "bs";

    /// <summary>"bs" or "en": whatever was given (null, another case, a language nobody has written words for) becomes one of the two.</summary>
    public static string Normalize(string? language) =>
        string.Equals(language?.Trim(), Bosnian, StringComparison.OrdinalIgnoreCase) ? Bosnian : English;

    public static bool IsBosnian(string? language) => Normalize(language) == Bosnian;

    /// <summary>The wording for a subject or a line: the Bosnian one for a person who chose Bosnian, the English one for everybody else.</summary>
    public static string Pick(string? language, string english, string bosnian) => IsBosnian(language) ? bosnian : english;
}
