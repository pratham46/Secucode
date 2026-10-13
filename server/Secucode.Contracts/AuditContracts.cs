namespace Secucode.Contracts;

public sealed record AuditEntryDto(long Id, int? ActorUserId, string? ActorName, string Action, string EntityType, string EntityId,
    int? ExamId, int? BranchId, string? Reason, string? BeforeJson, string? AfterJson, string? Client, string? Ip, DateTime OccurredAt);
