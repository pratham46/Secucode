namespace Secucode.Contracts;

public sealed record StartAttemptRequest(string Pin);

public sealed record AttemptDto(int Id, int ExamId, int StudentId, int AttemptNumber, string QuestionText, AttemptStatus Status, ClientKind Client,
    DateTime StartedAt, DateTime DeadlineAt, DateTime? SubmittedAt, string? Language, string Code, int Strikes, string? SubmitReason,
    DateTime ServerTime, DateTime? LastSavedAt);

public sealed record AttemptEventDto(long Id, AttemptEventType Type, DateTime OccurredAt, string? Detail, long? EvidenceId);

public sealed record EvidenceDto(long Id, EvidenceKind Kind, string ContentType, long SizeBytes, DateTime CreatedAt);

public sealed record AttemptDetailDto(AttemptDto Attempt, string StudentName, IReadOnlyList<AttemptEventDto> Events, IReadOnlyList<EvidenceDto> Evidence);

public sealed record AttemptSummaryDto(int Id, int StudentId, string StudentName, int AttemptNumber, AttemptStatus Status, ClientKind Client,
    DateTime StartedAt, DateTime DeadlineAt, DateTime? SubmittedAt, int Strikes);

public sealed record SaveCodeRequest(string Code, string Language);

public sealed record AttemptEventRequest(AttemptEventType Type, string? Detail, long? EvidenceId);

public sealed record SubmitRequest(string Code, string Language, string? Reason);
