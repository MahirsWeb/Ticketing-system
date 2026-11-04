using System.ComponentModel.DataAnnotations;

namespace TicketSystemTech.Api.Contracts;

public record KnowledgeBaseAskRequest(
    [Required, MaxLength(TicketSystemTech.Application.Common.AiPromptLimits.MaxQuestionChars)] string Question
);

public record KnowledgeBaseAskResponseDto(
    string Answer,
    List<KnowledgeBaseSearchResultDto> Sources,
    int SimilarTicketCount,
    int TotalEligibleTicketCount,
    double SimilarPercentage,
    // False when AI is switched off or not configured on this installation: the screen says so instead of presenting
    // keyword matches as if a model had answered.
    bool AiAvailable = true
);

public record SimilarTicketsResponseDto(
    int SimilarTicketCount,
    int TotalEligibleTicketCount,
    double SimilarPercentage,
    List<KnowledgeBaseSearchResultDto> Tickets
);

// A match can come from a resolved ticket or an uploaded documentation file — the ticket-only
// fields are null for a document match, and vice versa; SourceType tells the frontend which.
public record KnowledgeBaseSearchResultDto(
    string SourceType,
    Guid? TicketId,
    string? TicketNumber,
    string? Status,
    DateTime? ClosedAtUtc,
    string? ResolutionSummary,
    Guid? DocumentId,
    string? DocumentFileUrl,
    string Title,
    int MatchScore
);

public record KnowledgeBaseDocumentDto(
    Guid Id,
    string Title,
    string SourceFileName,
    string? FileUrl,
    DateTime CreatedAt,
    int ChunkCount
);
