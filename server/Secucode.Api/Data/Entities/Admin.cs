namespace Secucode.Api.Data.Entities;

public class SystemSettings
{
    public int Id { get; set; } = 1; // single row
    public bool AllowQuestionEditAfterAttempts { get; set; } = true;
    public bool AllowAttemptReset { get; set; } = true;
    public bool AllowAttemptReopen { get; set; } = true;
    public bool AllowTimeExtension { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

public class AuditEntry
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public int? ExamId { get; set; }
    public int? BranchId { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Client { get; set; }
    public string? Ip { get; set; }
    public DateTime OccurredAt { get; set; }
}
