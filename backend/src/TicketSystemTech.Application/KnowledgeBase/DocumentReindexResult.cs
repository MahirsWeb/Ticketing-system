namespace TicketSystemTech.Application.KnowledgeBase;

public sealed record DocumentReindexResult(bool Succeeded, string Message, int ChunkCount);
