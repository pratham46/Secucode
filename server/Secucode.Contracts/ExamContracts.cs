namespace Secucode.Contracts;

public sealed record QuestionInput(int? Id, string Text);

public sealed record QuestionDto(int Id, string Text, int SortOrder);

// Target: BranchId alone = whole department; + ClassId = that year; + BatchId = that batch.
public sealed record ExamRequest(string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, bool IsActive,
    int BranchId, int? ClassId, int? BatchId,
    bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension,
    IReadOnlyList<QuestionInput> Questions);

public sealed record ExamDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, bool IsActive,
    int BranchId, int? ClassId, int? BatchId, int CreatedBy, string CreatedByName,
    bool AllowQuestionEditAfterAttempts, bool AllowAttemptReset, bool AllowAttemptReopen, bool AllowTimeExtension,
    IReadOnlyList<QuestionDto>? Questions);

public sealed record StudentExamDto(int Id, string Title, DateTime StartsAt, DateTime EndsAt, int DurationMinutes, ExamMode Mode, int? InProgressAttemptId);

public sealed record SetActiveRequest(bool IsActive);
