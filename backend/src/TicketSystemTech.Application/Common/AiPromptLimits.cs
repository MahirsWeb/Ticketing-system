using System.Text;

namespace TicketSystemTech.Application.Common;

/// <summary>
/// What may be sent to the AI provider in one request. The provider charges by the amount of text, so each of these is a
/// ceiling on the cost of one question, whoever asks and whatever the ticket history happens to contain.
/// </summary>
public static class AiPromptLimits
{
    /// <summary>A question typed by a person. Far more than anybody writes; mostly there to stop a pasted document.</summary>
    public const int MaxQuestionChars = 2_000;

    /// <summary>One retrieved ticket or document passage. Longer ones are cut (the start of a ticket is what describes the problem).</summary>
    public const int MaxEntryChars = 6_000;

    /// <summary>Everything retrieved for one question. Entries beyond this are left out, best matches first.</summary>
    public const int MaxContextChars = 60_000;

    private const string Separator = "\n---\n";

    /// <summary>Joins the passages into the context for the model, cutting long ones and stopping at the total ceiling.</summary>
    public static string BuildContext(IEnumerable<string> entries)
    {
        var context = new StringBuilder();
        foreach (var entry in entries)
        {
            var text = entry.Length > MaxEntryChars ? entry[..MaxEntryChars] + "…" : entry;
            var needed = text.Length + (context.Length == 0 ? 0 : Separator.Length);
            if (context.Length > 0 && context.Length + needed > MaxContextChars) break;

            if (context.Length > 0) context.Append(Separator);
            context.Append(text);
        }

        return context.Length > MaxContextChars ? context.ToString(0, MaxContextChars) : context.ToString();
    }
}
