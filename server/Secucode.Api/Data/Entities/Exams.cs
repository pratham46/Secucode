using Secucode.Api.Auth;
using Secucode.Contracts;

namespace Secucode.Api.Data.Entities;

public class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int DurationMinutes { get; set; }
    public ExamMode Mode { get; set; }
    public bool IsActive { get; set; } = true;
    public int BranchId { get; set; }
    public int? ClassId { get; set; }
    public int? BatchId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool AllowQuestionEditAfterAttempts { get; set; } = true;
    public bool AllowAttemptReset { get; set; } = true;
    public bool AllowAttemptReopen { get; set; } = true;
    public bool AllowTimeExtension { get; set; } = true;
    public List<Question> Questions { get; set; } = [];

    public Scope Target => new(BranchId, ClassId, BatchId);
}

public class Question
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
}

public class Attempt
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public int AttemptNumber { get; set; }
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = ""; // snapshot: later question edits never change it
    public ClientKind Client { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public AttemptStatus Status { get; set; }
    public string? SubmitReason { get; set; }
    public string? Language { get; set; }
    public string Code { get; set; } = "";
    public int Strikes { get; set; }
    public DateTime? LastSavedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public int? AllowanceMinutes { get; set; } // set by a reset after the window closed
    public List<AttemptEvent> Events { get; set; } = [];
    public List<EvidenceFile> Evidence { get; set; } = [];
}

public class AttemptEvent
{
    public long Id { get; set; }
    public int AttemptId { get; set; }
    public AttemptEventType Type { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Detail { get; set; }
    public long? EvidenceId { get; set; }
}

public class EvidenceFile
{
    public long Id { get; set; }
    public int AttemptId { get; set; }
    public EvidenceKind Kind { get; set; }
    public string StoragePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
