using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Exams;

public static class ExamEndpoints
{
    private sealed record ValidExam(DateTime StartsAt, DateTime EndsAt, Scope Target, List<QuestionInput> Questions);

    public static void MapExams(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/exams");
        g.MapGet("", ListAsync);
        g.MapGet("/{id:int}", GetAsync);
        g.MapPost("", CreateAsync);
        g.MapPut("/{id:int}", UpdateAsync);
        g.MapPatch("/{id:int}/active", SetActiveAsync);
        g.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(AppDbContext db, Caller caller, TimeProvider time, DateTime? from, DateTime? to, int? branchId)
    {
        if (!await caller.IsStaffAsync())
        {
            var scope = await caller.StudentScopeAsync() ?? throw ApiException.Forbidden("You have no active role.");
            return Results.Ok(await StudentListAsync(db, caller.UserId, scope, time.UtcNow()));
        }

        var q = db.Exams.AsNoTracking();
        if (from is not null) q = q.Where(e => e.EndsAt >= ToUtc(from.Value));
        if (to is not null) q = q.Where(e => e.StartsAt <= ToUtc(to.Value));
        if (branchId is not null) q = q.Where(e => e.BranchId == branchId);
        var rows = await q.OrderBy(e => e.StartsAt)
            .Select(e => new { Exam = e, Creator = db.Users.Where(u => u.Id == e.CreatedBy).Select(u => u.FullName).First() })
            .ToListAsync();

        // ponytail: visibility filtered in memory; fine for a college's exam volume, push into SQL if it grows large
        var visible = new List<ExamDto>();
        foreach (var r in rows)
            if (await caller.CanOnExamAsync(Capability.ViewResults, r.Exam) || await caller.CanOnExamAsync(Capability.ManageExams, r.Exam))
                visible.Add(ExamMapping.ToDto(r.Exam, r.Creator, includeQuestions: false));
        return Results.Ok(visible);
    }

    private static async Task<List<StudentExamDto>> StudentListAsync(AppDbContext db, int studentId, Scope scope, DateTime now)
    {
        var attempts = await db.Attempts.Where(a => a.StudentId == studentId)
            .Select(a => new { a.Id, a.ExamId, a.AttemptNumber, a.Status, a.AllowanceMinutes }).ToListAsync();
        var latest = attempts.GroupBy(a => a.ExamId).ToDictionary(g => g.Key, g => g.MaxBy(a => a.AttemptNumber)!);
        var allowanceExamIds = latest.Values.Where(a => a.Status == AttemptStatus.Voided && a.AllowanceMinutes != null).Select(a => a.ExamId).ToList();

        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.IsActive && (e.EndsAt > now || allowanceExamIds.Contains(e.Id)))
            .OrderBy(e => e.StartsAt).ToListAsync();

        return exams
            .Where(e => e.Target.Contains(scope))
            .Where(e => !latest.TryGetValue(e.Id, out var a) || a.Status is AttemptStatus.InProgress or AttemptStatus.Voided)
            .Select(e => new StudentExamDto(e.Id, e.Title, e.StartsAt, e.EndsAt, e.DurationMinutes, e.Mode,
                latest.TryGetValue(e.Id, out var a) && a.Status == AttemptStatus.InProgress ? a.Id : null))
            .ToList();
    }

    private static async Task<IResult> GetAsync(int id, AppDbContext db, Caller caller)
    {
        var exam = await db.Exams.Include(e => e.Questions).AsNoTracking().SingleOrDefaultAsync(e => e.Id == id)
            ?? throw ApiException.NotFound("Exam");

        if (await caller.IsStaffAsync())
        {
            if (!await caller.CanOnExamAsync(Capability.ViewResults, exam) && !await caller.CanOnExamAsync(Capability.ManageExams, exam))
                throw ApiException.NotFound("Exam");
            return Results.Ok(await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true));
        }

        var scope = await caller.StudentScopeAsync();
        if (scope is null || !exam.IsActive || !exam.Target.Contains(scope.Value))
            throw ApiException.NotFound("Exam");
        var userId = caller.UserId;
        var inProgress = await db.Attempts.Where(a => a.ExamId == id && a.StudentId == userId && a.Status == AttemptStatus.InProgress)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync();
        return Results.Ok(new StudentExamDto(exam.Id, exam.Title, exam.StartsAt, exam.EndsAt, exam.DurationMinutes, exam.Mode, inProgress));
    }

    private static async Task<IResult> CreateAsync(ExamRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var input = Validate(req);
        await caller.RequireAsync(Capability.CreateExams, input.Target, "You can't create exams for that department, class or batch.");
        await EnsureTargetExistsAsync(db, input.Target);

        var exam = new Exam { CreatedBy = caller.UserId, CreatedAt = time.UtcNow() };
        Apply(exam, req, input);
        for (var i = 0; i < input.Questions.Count; i++)
            exam.Questions.Add(new Question { Text = input.Questions[i].Text, SortOrder = i });

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "exam.create", "Exam", exam.Id, after: ExamMapping.Snapshot(exam), examId: exam.Id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/exams/{exam.Id}", await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true));
    }

    private static async Task<ExamDto> UpdateAsync(int id, ExamRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ManageExams, exam, "You can't edit this exam.");
        var input = Validate(req);

        if (input.Target != exam.Target)
        {
            if (!await caller.CanAsync(Capability.CreateExams, input.Target) && !await caller.CanAsync(Capability.ManageExams, input.Target))
                throw ApiException.Forbidden("You can't move this exam to that department, class or batch.");
            await EnsureTargetExistsAsync(db, input.Target);
        }

        var usedQuestionIds = (await db.Attempts.Where(a => a.ExamId == id).Select(a => a.QuestionId).Distinct().ToListAsync()).ToHashSet();
        if (usedQuestionIds.Count > 0 && QuestionsChanged(exam, input.Questions))
        {
            await caller.RequireOnExamAsync(Capability.EditQuestionsAfterAttempts, exam, "You can't edit questions after students have started.");
            await Switches.EnsureOnAsync(db, exam, Switch.QuestionEdit);
        }

        var before = ExamMapping.Snapshot(exam);
        Apply(exam, req, input);
        SyncQuestions(exam, input.Questions, usedQuestionIds);
        audit.Record(caller.UserId, "exam.update", "Exam", exam.Id, before, ExamMapping.Snapshot(exam), examId: exam.Id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        return await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true);
    }

    private static async Task<ExamDto> SetActiveAsync(int id, SetActiveRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ManageExams, exam, "You can't change this exam.");
        var before = new { exam.IsActive };
        exam.IsActive = req.IsActive;
        audit.Record(caller.UserId, "exam.set_active", "Exam", exam.Id, before, new { exam.IsActive }, examId: exam.Id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        return await ExamMapping.ToDtoAsync(db, exam, includeQuestions: true);
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ManageExams, exam, "You can't delete this exam.");
        if (await db.Attempts.AnyAsync(a => a.ExamId == id))
            throw ApiException.Conflict("Students have attempted this exam, so it can't be deleted. Deactivate it instead.");

        var before = ExamMapping.Snapshot(exam);
        db.Exams.Remove(exam);
        audit.Record(caller.UserId, "exam.delete", "Exam", id, before, examId: id, branchId: exam.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static ValidExam Validate(ExamRequest req)
    {
        var questions = (req.Questions ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q.Text))
            .Select(q => q with { Text = q.Text.Trim() })
            .ToList();
        var starts = ToUtc(req.StartsAt);
        var ends = ToUtc(req.EndsAt);
        var target = new Scope(req.BranchId, req.ClassId, req.BatchId);

        new Validator()
            .Rule(!string.IsNullOrWhiteSpace(req.Title) && req.Title.Trim().Length <= 255, "title", "Title is required (up to 255 characters).")
            .Rule(ends > starts, "endsAt", "The exam must end after it starts.")
            .Rule(req.DurationMinutes is >= 1 and <= 1440, "durationMinutes", "Duration must be between 1 and 1440 minutes.")
            .Rule(target.IsValidShape, "batchId", "A batch needs a class.")
            .Rule(Enum.IsDefined(req.Mode), "mode", "Unknown exam mode.")
            .Rule(questions.Count > 0, "questions", "Add at least one question.")
            .Rule(questions.All(q => q.Text.Length <= 10_000), "questions", "Each question can be up to 10,000 characters.")
            .ThrowIfInvalid();
        return new ValidExam(starts, ends, target, questions);
    }

    // Clients may send times without a zone (e.g. from a form). We store UTC, so treat unzoned values as UTC.
    private static DateTime ToUtc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static void Apply(Exam exam, ExamRequest req, ValidExam input)
    {
        exam.Title = req.Title.Trim();
        exam.StartsAt = input.StartsAt;
        exam.EndsAt = input.EndsAt;
        exam.DurationMinutes = req.DurationMinutes;
        exam.Mode = req.Mode;
        exam.IsActive = req.IsActive;
        exam.BranchId = input.Target.BranchId!.Value;
        exam.ClassId = input.Target.ClassId;
        exam.BatchId = input.Target.BatchId;
        exam.AllowQuestionEditAfterAttempts = req.AllowQuestionEditAfterAttempts;
        exam.AllowAttemptReset = req.AllowAttemptReset;
        exam.AllowAttemptReopen = req.AllowAttemptReopen;
        exam.AllowTimeExtension = req.AllowTimeExtension;
    }

    private static bool QuestionsChanged(Exam exam, List<QuestionInput> inputs)
    {
        var current = exam.Questions.Where(q => !q.IsDeleted).OrderBy(q => q.SortOrder).ToList();
        return current.Count != inputs.Count || current.Where((q, i) => inputs[i].Id != q.Id || inputs[i].Text != q.Text).Any();
    }

    // Matches inputs to existing questions by id; questions given to an attempt are soft-deleted, never removed.
    private static void SyncQuestions(Exam exam, List<QuestionInput> inputs, HashSet<int> usedQuestionIds)
    {
        var live = exam.Questions.Where(q => !q.IsDeleted).ToDictionary(q => q.Id);
        var kept = new HashSet<int>();
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].Id is int qid && live.TryGetValue(qid, out var q))
            {
                q.Text = inputs[i].Text;
                q.SortOrder = i;
                kept.Add(qid);
            }
            else
            {
                exam.Questions.Add(new Question { Text = inputs[i].Text, SortOrder = i });
            }
        }
        foreach (var q in live.Values.Where(q => !kept.Contains(q.Id)))
        {
            if (usedQuestionIds.Contains(q.Id))
                q.IsDeleted = true;
            else
                exam.Questions.Remove(q); // orphan of a required relationship → deleted on save
        }
    }

    private static async Task EnsureTargetExistsAsync(AppDbContext db, Scope s)
    {
        var v = new Validator();
        v.Rule(s.BranchId is int b && await db.Branches.AnyAsync(x => x.Id == b), "branchId", "That department doesn't exist.");
        if (s.ClassId is int c) v.Rule(await db.Classes.AnyAsync(x => x.Id == c), "classId", "That class doesn't exist.");
        if (s.BatchId is int t) v.Rule(await db.Batches.AnyAsync(x => x.Id == t), "batchId", "That batch doesn't exist.");
        v.ThrowIfInvalid();
    }
}
