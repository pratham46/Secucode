using Microsoft.EntityFrameworkCore;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Exams;

public static class ExamMapping
{
    public static async Task<ExamDto> ToDtoAsync(AppDbContext db, Exam e, bool includeQuestions)
    {
        var creator = await db.Users.Where(u => u.Id == e.CreatedBy).Select(u => u.FullName).SingleAsync();
        return ToDto(e, creator, includeQuestions);
    }

    public static ExamDto ToDto(Exam e, string creatorName, bool includeQuestions) => new(
        e.Id, e.Title, e.StartsAt, e.EndsAt, e.DurationMinutes, e.Mode, e.IsActive, e.BranchId, e.ClassId, e.BatchId,
        e.CreatedBy, creatorName, e.AllowQuestionEditAfterAttempts, e.AllowAttemptReset, e.AllowAttemptReopen, e.AllowTimeExtension,
        includeQuestions
            ? e.Questions.Where(q => !q.IsDeleted).OrderBy(q => q.SortOrder).Select(q => new QuestionDto(q.Id, q.Text, q.SortOrder)).ToList()
            : null);

    public static object Snapshot(Exam e) => new
    {
        e.Title, e.StartsAt, e.EndsAt, e.DurationMinutes, e.Mode, e.IsActive, e.BranchId, e.ClassId, e.BatchId,
        e.AllowQuestionEditAfterAttempts, e.AllowAttemptReset, e.AllowAttemptReopen, e.AllowTimeExtension,
        Questions = e.Questions.Where(q => !q.IsDeleted).OrderBy(q => q.SortOrder).Select(q => q.Text).ToList(),
    };
}
