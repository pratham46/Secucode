using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Features.Exams;
using Secucode.Api.Features.Live;
using Secucode.Contracts;

namespace Secucode.Api.Features.Corrections;

// Spec §6. Order of checks: capability (403) → switch (403) → input (400) → state (409).
public static class CorrectionEndpoints
{
    public static void MapCorrections(this IEndpointRouteBuilder api)
    {
        api.MapPost("/attempts/{id:int}/reset", ResetAsync);
        api.MapPost("/attempts/{id:int}/reopen", ReopenAsync);
        api.MapPost("/attempts/{id:int}/extend", ExtendAttemptAsync);
        api.MapPost("/exams/{id:int}/extend", ExtendExamAsync);
    }

    private static async Task<AttemptDto> ResetAsync(int id, ResetAttemptRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        TimeProvider time, LiveNotifier live)
    {
        var a = await LoadAsync(db, id);
        await caller.RequireOnExamAsync(Capability.ResetAttempts, a.Exam, "You can't reset attempts on this exam.");
        await Switches.EnsureOnAsync(db, a.Exam, Switch.Reset);
        var now = time.UtcNow();
        Reason(req.Reason)
            .Rule(req.ExtraMinutes is null or (>= 1 and <= 240), "extraMinutes", "Extra minutes must be between 1 and 240.")
            .Rule(now < a.Exam.EndsAt || req.ExtraMinutes is not null, "extraMinutes", "The exam window has closed, so give the student extra minutes.")
            .ThrowIfInvalid();
        if (a.Status == AttemptStatus.Voided)
            throw ApiException.Conflict("This attempt is already voided.");

        var before = Snapshot(a);
        a.Status = AttemptStatus.Voided;
        a.VoidedAt = now;
        a.VoidedBy = caller.UserId;
        a.VoidReason = req.Reason.Trim();
        a.AllowanceMinutes = req.ExtraMinutes;
        audit.Record(caller.UserId, "attempt.reset", "Attempt", a.Id, before, Snapshot(a), a.VoidReason, a.ExamId, a.Exam.BranchId);
        await db.SaveChangesAsync();
        await live.AttemptVoidedAsync(a);
        return a.ToDto(now);
    }

    private static async Task<AttemptDto> ReopenAsync(int id, ReopenAttemptRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        TimeProvider time, LiveNotifier live)
    {
        var a = await LoadAsync(db, id);
        await caller.RequireOnExamAsync(Capability.ReopenAttempts, a.Exam, "You can't reopen attempts on this exam.");
        await Switches.EnsureOnAsync(db, a.Exam, Switch.Reopen);
        Reason(req.Reason).Rule(req.ExtraMinutes is >= 1 and <= 240, "extraMinutes", "Extra minutes must be between 1 and 240.").ThrowIfInvalid();
        if (a.Status is not (AttemptStatus.Submitted or AttemptStatus.AutoSubmitted))
            throw ApiException.Conflict("Only submitted attempts can be reopened.");

        var now = time.UtcNow();
        var before = Snapshot(a);
        a.Status = AttemptStatus.InProgress;
        a.DeadlineAt = now.AddMinutes(req.ExtraMinutes);
        a.SubmittedAt = null;
        a.SubmitReason = null;
        audit.Record(caller.UserId, "attempt.reopen", "Attempt", a.Id, before, Snapshot(a), req.Reason.Trim(), a.ExamId, a.Exam.BranchId);
        await db.SaveChangesAsync();
        await live.AttemptReopenedAsync(a);
        return a.ToDto(now);
    }

    private static async Task<AttemptDto> ExtendAttemptAsync(int id, ExtendAttemptRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        TimeProvider time, LiveNotifier live)
    {
        var a = await LoadAsync(db, id);
        await caller.RequireOnExamAsync(Capability.ExtendTime, a.Exam, "You can't extend time on this exam.");
        await Switches.EnsureOnAsync(db, a.Exam, Switch.Extension);
        Reason(req.Reason).Rule(req.Minutes is >= 1 and <= 240, "minutes", "Minutes must be between 1 and 240.").ThrowIfInvalid();
        if (a.Status != AttemptStatus.InProgress)
            throw ApiException.Conflict("Only attempts in progress can be extended. Reopen it instead.");

        var before = Snapshot(a);
        a.DeadlineAt = a.DeadlineAt.AddMinutes(req.Minutes);
        audit.Record(caller.UserId, "attempt.extend", "Attempt", a.Id, before, Snapshot(a), req.Reason.Trim(), a.ExamId, a.Exam.BranchId);
        await db.SaveChangesAsync();
        await live.DeadlineChangedAsync(a);
        return a.ToDto(time.UtcNow());
    }

    private static async Task<ExtendExamResult> ExtendExamAsync(int id, ExtendExamRequest req, AppDbContext db, Caller caller, AuditWriter audit,
        LiveNotifier live)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == id) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ExtendTime, exam, "You can't extend time on this exam.");
        await Switches.EnsureOnAsync(db, exam, Switch.Extension);
        Reason(req.Reason).Rule(req.Minutes is >= 1 and <= 240, "minutes", "Minutes must be between 1 and 240.").ThrowIfInvalid();

        var running = await db.Attempts.Where(a => a.ExamId == id && a.Status == AttemptStatus.InProgress).ToListAsync();
        var before = new { exam.EndsAt, Attempts = running.Select(a => new { a.Id, a.DeadlineAt }).ToList() };
        foreach (var a in running)
            a.DeadlineAt = a.DeadlineAt.AddMinutes(req.Minutes);
        if (req.ExtendWindow)
            exam.EndsAt = exam.EndsAt.AddMinutes(req.Minutes);
        var after = new { exam.EndsAt, Attempts = running.Select(a => new { a.Id, a.DeadlineAt }).ToList() };
        audit.Record(caller.UserId, "exam.extend", "Exam", exam.Id, before, after, req.Reason.Trim(), exam.Id, exam.BranchId);
        await db.SaveChangesAsync();

        foreach (var a in running)
            await live.DeadlineChangedAsync(a);
        return new ExtendExamResult(running.Count, exam.EndsAt);
    }

    private static async Task<Attempt> LoadAsync(AppDbContext db, int id) =>
        await db.Attempts.Include(a => a.Exam).SingleOrDefaultAsync(a => a.Id == id) ?? throw ApiException.NotFound("Attempt");

    private static Validator Reason(string? reason) => new Validator()
        .Rule(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500, "reason", "A reason (up to 500 characters) is required.");

    private static object Snapshot(Attempt a) => new { a.Status, a.DeadlineAt, a.SubmittedAt, a.AllowanceMinutes };
}
