using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Attempts;

public static class AttemptMapping
{
    public static AttemptDto ToDto(this Attempt a, DateTime serverTime) => new(a.Id, a.ExamId, a.StudentId, a.AttemptNumber, a.QuestionText,
        a.Status, a.Client, a.StartedAt, a.DeadlineAt, a.SubmittedAt, a.Language, a.Code, a.Strikes, a.SubmitReason, serverTime, a.LastSavedAt);

    public static AttemptEventDto ToDto(this AttemptEvent e) => new(e.Id, e.Type, e.OccurredAt, e.Detail, e.EvidenceId);

    public static EvidenceDto ToDto(this EvidenceFile f) => new(f.Id, f.Kind, f.ContentType, f.SizeBytes, f.CreatedAt);
}
