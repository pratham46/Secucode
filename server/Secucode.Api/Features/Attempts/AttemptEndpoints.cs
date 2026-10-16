using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Live;
using Secucode.Contracts;

namespace Secucode.Api.Features.Attempts;

public static class AttemptEndpoints
{
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);
    private const long MaxEvidenceBytes = 10 * 1024 * 1024;
    private const int MaxCodeChars = 256 * 1024;

    private static readonly HashSet<AttemptEventType> StrikeTypes =
    [
        AttemptEventType.FaceMissing, AttemptEventType.MultipleFaces, AttemptEventType.FaceMismatch, AttemptEventType.FocusLost,
        AttemptEventType.Minimized, AttemptEventType.TabHidden, AttemptEventType.FullscreenExited,
    ];

    private static readonly Dictionary<string, string> EvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["audio/wav"] = ".wav", ["audio/x-wav"] = ".wav",
        ["audio/webm"] = ".webm", // browsers record Opus in WebM (web exams)
    };

    public static void MapAttempts(this IEndpointRouteBuilder api)
    {
        api.MapPost("/exams/{examId:int}/attempts", StartAsync);
        api.MapGet("/exams/{examId:int}/attempts", ListForExamAsync);
        var g = api.MapGroup("/attempts/{id:int}");
        g.MapGet("", GetAsync);
        g.MapPut("/code", SaveCodeAsync);
        g.MapPost("/events", RecordEventAsync);
        g.MapPost("/evidence", UploadEvidenceAsync).DisableAntiforgery(); // bearer-token API: no cookies to forge
        g.MapPost("/submit", SubmitAsync);
        api.MapGet("/evidence/{id:long}", DownloadEvidenceAsync);
    }

    // Spec §8 step 1, checks in the spec's order.
    private static async Task<IResult> StartAsync(int examId, StartAttemptRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live, LoginThrottle throttle)
    {
        var me = await ActiveUserAsync(db, caller);
        var scope = await caller.StudentScopeAsync() ?? throw ApiException.Forbidden("Only students can take exams.");
        var pinKey = $"pin:{me.Id}";
        using (await throttle.AcquireAsync(pinKey))
        {
            if (throttle.IsLocked(pinKey))
                throw ApiException.TooManyRequests("Too many failed attempts. Try again in a few minutes.");
            if (me.PinHash is null || string.IsNullOrEmpty(req.Pin) || !BCrypt.Net.BCrypt.Verify(req.Pin, me.PinHash))
            {
                throttle.Fail(pinKey);
                throw ApiException.Forbidden("Incorrect PIN.");
            }
            throttle.Succeed(pinKey);
        }

        var exam = await db.Exams.Include(e => e.Questions).SingleOrDefaultAsync(e => e.Id == examId);
        if (exam is null || !exam.Target.Contains(scope))
            throw ApiException.NotFound("Exam");
        if (!exam.IsActive)
            throw ApiException.Forbidden("This exam is not active.");

        var now = time.UtcNow();
        var mine = await db.Attempts.Where(a => a.ExamId == examId && a.StudentId == me.Id).OrderBy(a => a.AttemptNumber).ToListAsync();
        var current = mine.LastOrDefault(a => a.Status != AttemptStatus.Voided);
        if (current is not null)
            throw ApiException.Conflict(current.Status == AttemptStatus.InProgress
                ? $"You already have an attempt in progress (id {current.Id})."
                : "You have already submitted this exam.");

        var allowance = mine.LastOrDefault() is { Status: AttemptStatus.Voided, AllowanceMinutes: int extra } ? extra : (int?)null;
        var windowOpen = now >= exam.StartsAt && now < exam.EndsAt;
        if (!windowOpen && allowance is null)
            throw ApiException.Forbidden(now < exam.StartsAt ? "This exam hasn't started yet." : "This exam has ended.");
        if (caller.Client == ClientKind.Web && exam.Mode == ExamMode.DesktopOnly)
            throw ApiException.Forbidden("This exam can only be taken in the SecuCode desktop app.");

        var questions = exam.Questions.Where(q => !q.IsDeleted).ToList();
        if (questions.Count == 0)
            throw ApiException.Conflict("This exam has no questions yet.");
        var question = questions[Random.Shared.Next(questions.Count)];

        var normalDeadline = now.AddMinutes(exam.DurationMinutes);
        var attempt = new Attempt
        {
            ExamId = examId,
            StudentId = me.Id,
            AttemptNumber = mine.Count + 1, // unique index turns a racing duplicate into a 409
            QuestionId = question.Id,
            QuestionText = question.Text,
            Client = caller.Client,
            StartedAt = now,
            DeadlineAt = windowOpen ? (normalDeadline < exam.EndsAt ? normalDeadline : exam.EndsAt) : now.AddMinutes(allowance!.Value),
            Status = AttemptStatus.InProgress,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        await live.AttemptStartedAsync(attempt);
        return Results.Created($"/api/attempts/{attempt.Id}", attempt.ToDto(now));
    }

    private static async Task<AttemptDetailDto> GetAsync(int id, AppDbContext db, Caller caller, TimeProvider time)
    {
        var a = await db.Attempts.Include(x => x.Exam).Include(x => x.Student).Include(x => x.Events).Include(x => x.Evidence)
            .AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Attempt");
        if (a.StudentId != caller.UserId && !await caller.CanOnExamAsync(Capability.ViewResults, a.Exam))
            throw ApiException.NotFound("Attempt");
        return new AttemptDetailDto(a.ToDto(time.UtcNow()), a.Student.FullName,
            a.Events.OrderBy(e => e.OccurredAt).Select(e => e.ToDto()).ToList(),
            a.Evidence.OrderBy(e => e.CreatedAt).Select(e => e.ToDto()).ToList());
    }

    private static async Task<IResult> SaveCodeAsync(int id, SaveCodeRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var a = await WritableAsync(db, caller, id, time);
        ValidateCode(req.Code, req.Language);
        var saved = time.UtcNow();
        // Guarded in SQL so a save that loses the race with submit can't overwrite the submitted code.
        CloseGuard(await db.Attempts.Where(x => x.Id == id && x.Status == AttemptStatus.InProgress)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Code, req.Code).SetProperty(x => x.Language, req.Language).SetProperty(x => x.LastSavedAt, saved)));
        await db.Entry(a).ReloadAsync();
        await live.CodeSavedAsync(a);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordEventAsync(int id, AttemptEventRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var a = await WritableAsync(db, caller, id, time);
        new Validator()
            .Rule(Enum.IsDefined(req.Type), "type", "Unknown event type.")
            .Rule((req.Detail?.Length ?? 0) <= 500, "detail", "Detail can be up to 500 characters.")
            .Rule(req.EvidenceId is null || await db.EvidenceFiles.AnyAsync(f => f.Id == req.EvidenceId && f.AttemptId == id), "evidenceId", "That evidence doesn't belong to this attempt.")
            .ThrowIfInvalid();

        var e = new AttemptEvent { AttemptId = id, Type = req.Type, OccurredAt = time.UtcNow(), Detail = req.Detail, EvidenceId = req.EvidenceId };
        db.AttemptEvents.Add(e);
        await db.SaveChangesAsync();
        if (StrikeTypes.Contains(req.Type)) // atomic increment: concurrent events can't lose a strike
            await db.Attempts.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Strikes, x => x.Strikes + 1));
        await db.Entry(a).ReloadAsync();
        await live.EventRecordedAsync(a, req.Type);
        return Results.Created($"/api/attempts/{id}", e.ToDto());
    }

    private static async Task<IResult> UploadEvidenceAsync(int id, [FromForm] string kind, IFormFile file, AppDbContext db, Caller caller,
        TimeProvider time, EvidenceStore store, CancellationToken ct)
    {
        var a = await WritableAsync(db, caller, id, time);
        var extension = EvidenceTypes.GetValueOrDefault(file.ContentType ?? "");
        new Validator()
            .Rule(Enum.TryParse<EvidenceKind>(kind, ignoreCase: true, out _), "kind", "Kind must be ReferenceFace, Webcam, Screenshot or Audio.")
            .Rule(file.Length is > 0 and <= MaxEvidenceBytes, "file", "File must be between 1 byte and 10 MB.")
            .Rule(extension is not null, "file", "Only JPEG, PNG, WAV and WebM files are accepted.")
            .ThrowIfInvalid();

        await using var content = file.OpenReadStream();
        var path = await store.SaveAsync(a.ExamId, a.Id, content, extension!, ct);
        var evidence = new EvidenceFile
        {
            AttemptId = a.Id, Kind = Enum.Parse<EvidenceKind>(kind, ignoreCase: true), StoragePath = path,
            ContentType = file.ContentType!, SizeBytes = file.Length, CreatedAt = time.UtcNow(),
        };
        db.EvidenceFiles.Add(evidence);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/evidence/{evidence.Id}", evidence.ToDto());
    }

    private static async Task<AttemptDto> SubmitAsync(int id, SubmitRequest req, AppDbContext db, Caller caller, TimeProvider time, LiveNotifier live)
    {
        var a = await WritableAsync(db, caller, id, time);
        ValidateCode(req.Code, req.Language);
        var now = time.UtcNow();
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? "Submitted by the student." : req.Reason.Trim();
        reason = reason.Length > 255 ? reason[..255] : reason;
        CloseGuard(await db.Attempts.Where(x => x.Id == id && x.Status == AttemptStatus.InProgress)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Code, req.Code).SetProperty(x => x.Language, req.Language).SetProperty(x => x.LastSavedAt, now)
                .SetProperty(x => x.Status, AttemptStatus.Submitted).SetProperty(x => x.SubmittedAt, now).SetProperty(x => x.SubmitReason, reason)));
        await db.Entry(a).ReloadAsync();
        await live.AttemptSubmittedAsync(a);
        return a.ToDto(now);
    }

    private static async Task<List<AttemptSummaryDto>> ListForExamAsync(int examId, AppDbContext db, Caller caller)
    {
        var exam = await db.Exams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId) ?? throw ApiException.NotFound("Exam");
        await caller.RequireOnExamAsync(Capability.ViewResults, exam, "You can't view results for this exam.");
        return await db.Attempts.Where(a => a.ExamId == examId).OrderBy(a => a.StartedAt)
            .Select(a => new AttemptSummaryDto(a.Id, a.StudentId, a.Student.FullName, a.AttemptNumber, a.Status, a.Client,
                a.StartedAt, a.DeadlineAt, a.SubmittedAt, a.Strikes))
            .ToListAsync();
    }

    private static async Task<IResult> DownloadEvidenceAsync(long id, AppDbContext db, Caller caller, EvidenceStore store)
    {
        var file = await db.EvidenceFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id) ?? throw ApiException.NotFound("Evidence");
        var attempt = await db.Attempts.Include(a => a.Exam).AsNoTracking().SingleAsync(a => a.Id == file.AttemptId);

        var ownReferenceFace = attempt.StudentId == caller.UserId && file.Kind == EvidenceKind.ReferenceFace;
        if (!ownReferenceFace && !await caller.CanOnExamAsync(Capability.ViewEvidence, attempt.Exam))
            throw ApiException.NotFound("Evidence");
        try { return Results.File(store.OpenRead(file.StoragePath), file.ContentType); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw ApiException.NotFound("Evidence"); }
    }

    // A deactivated student keeps a valid access token for up to 15 minutes; stop them here.
    private static async Task<User> ActiveUserAsync(AppDbContext db, Caller caller)
    {
        var userId = caller.UserId;
        return await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw ApiException.Unauthorized("This account is deactivated.");
    }

    private static async Task<Attempt> WritableAsync(AppDbContext db, Caller caller, int id, TimeProvider time)
    {
        await ActiveUserAsync(db, caller);
        var a = await db.Attempts.SingleOrDefaultAsync(x => x.Id == id);
        if (a is null || a.StudentId != caller.UserId)
            throw ApiException.NotFound("Attempt");
        if (a.Status != AttemptStatus.InProgress)
            throw ApiException.Conflict("This attempt is closed.");
        if (time.UtcNow() > a.DeadlineAt + SubmitGrace)
            throw ApiException.Conflict("Time is up for this attempt.");
        return a;
    }

    private static void CloseGuard(int rows)
    {
        if (rows == 0)
            throw ApiException.Conflict("This attempt is closed.");
    }

    private static void ValidateCode(string? code, string? language) => new Validator()
        .Rule(code is not null && code.Length <= MaxCodeChars, "code", "Code is required and must be under 256 KB.")
        .Rule(!string.IsNullOrWhiteSpace(language) && language.Length <= 20, "language", "Language is required.")
        .ThrowIfInvalid();
}
