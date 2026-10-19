namespace TicketSystemTech.Application.KnowledgeBase;

public static class KnowledgeBaseRules
{
    /// <summary>
    /// A ticket only qualifies as an AI source once its internal notes carry at least this many characters (the
    /// measure is the summed length of the notes' stored HTML, the same figure the search query filters on) —
    /// screens out tickets that were never really documented internally. The indexer and the reindex job use the
    /// same rule, so nothing is embedded (and sent to the embedding provider) that search could never return.
    /// </summary>
    public const int MinInternalNoteChars = 100;
}
