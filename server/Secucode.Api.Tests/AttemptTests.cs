using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AttemptTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private sealed record Setup(Section Section, TestUser Author, TestUser Student, int ExamId);

    private async Task<Setup> SetupAsync(ExamMode mode = ExamMode.WebAllowed, TimeSpan? startsIn = null, TimeSpan? window = null, int durationMinutes = 30)
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Year, mode, startsIn, window, durationMinutes);
        return new Setup(s, author, student, exam.Id);
    }

    private static Task<HttpResponseMessage> Start(HttpClient c, int examId, string pin = World.Pin) =>
        c.PostJsonAsync($"/api/exams/{examId}/attempts", new StartAttemptRequest(pin));

    [Fact]
    public async Task Starting_snapshots_a_question_and_caps_the_deadline_at_the_window_end()
    {
        var x = await SetupAsync(window: TimeSpan.FromMinutes(15), durationMinutes: 60); // window ends 10 minutes from now
        var student = await _world.LoginAsync(x.Student, ClientKind.Desktop);

        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        Assert.Contains(attempt.QuestionText, new[] { "Question 1", "Question 2" });
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Equal(ClientKind.Desktop, attempt.Client);
        Assert.Equal(_world.Now.AddMinutes(10), attempt.DeadlineAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Repeated_wrong_pins_lock_the_student_out_even_for_the_right_pin()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        for (var i = 0; i < 5; i++)
            await (await Start(student, x.ExamId, pin: "0000")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await Start(student, x.ExamId)).ExpectProblemAsync(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_wrong_pin_or_a_staff_account_cannot_start()
    {
        var x = await SetupAsync();
        await (await Start(await _world.LoginAsync(x.Student), x.ExamId, pin: "0000")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        Assert.Contains("Only students", await (await Start(await _world.LoginAsync(x.Author), x.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Students_outside_the_target_get_404()
    {
        var x = await SetupAsync();
        var other = await _world.SectionAsync();
        var outsider = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, other.Batch));
        await (await Start(outsider, x.ExamId)).ExpectProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_exam_window_and_active_flag_are_enforced()
    {
        var early = await SetupAsync(startsIn: TimeSpan.FromHours(1));
        Assert.Contains("hasn't started", await (await Start(await _world.LoginAsync(early.Student), early.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden));

        var late = await SetupAsync(startsIn: TimeSpan.FromHours(-3), window: TimeSpan.FromHours(1));
        Assert.Contains("ended", await (await Start(await _world.LoginAsync(late.Student), late.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden));

        var off = await SetupAsync();
        await _world.WithDb(db => db.Exams.Where(e => e.Id == off.ExamId).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false)));
        await (await Start(await _world.LoginAsync(off.Student), off.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Desktop_only_exams_reject_the_web_client()
    {
        var x = await SetupAsync(ExamMode.DesktopOnly);
        var web = await (await Start(await _world.LoginAsync(x.Student, ClientKind.Web), x.ExamId)).ExpectProblemAsync(HttpStatusCode.Forbidden);
        Assert.Contains("desktop app", web);
        await (await Start(await _world.LoginAsync(x.Student, ClientKind.Desktop), x.ExamId)).ExpectAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Only_one_attempt_at_a_time_even_when_requests_race()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);

        var results = await Task.WhenAll(Start(student, x.ExamId), Start(student, x.ExamId));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).OrderBy(c => c));
        await (await Start(student, x.ExamId)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Autosave_events_and_submit_follow_the_server_clock()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("print('नमस्ते 🙂')", "Python"))).ExpectAsync(HttpStatusCode.NoContent);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/events", new AttemptEventRequest(AttemptEventType.FaceMissing, null, null))).ExpectAsync(HttpStatusCode.Created);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/events", new AttemptEventRequest(AttemptEventType.AudioUnavailable, "no mic", null))).ExpectAsync(HttpStatusCode.Created);

        var detail = await (await student.GetAsync($"/api/attempts/{attempt.Id}")).ReadAsync<AttemptDetailDto>();
        Assert.Equal("print('नमस्ते 🙂')", detail.Attempt.Code); // utf8mb4 round-trip
        Assert.Null(attempt.LastSavedAt);
        Assert.NotNull(detail.Attempt.LastSavedAt);                // the web client compares its local copy against this
        Assert.Equal(1, detail.Attempt.Strikes);                  // AudioUnavailable isn't a strike
        Assert.Equal(2, detail.Events.Count);

        api.Time.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(20)); // past the deadline, inside the 30 s grace
        student = await _world.LoginAsync(x.Student);                           // the 15-minute access token expired meanwhile
        var submitted = await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("print(2)", "Python", null))).ReadAsync<AttemptDto>();
        Assert.Equal(AttemptStatus.Submitted, submitted.Status);
        Assert.Equal(DateTimeKind.Utc, submitted.SubmittedAt!.Value.Kind);
        var reloaded = await _world.ReloadAttemptAsync(attempt.Id); // nullable DateTimes come back as UTC like the others
        Assert.Equal(DateTimeKind.Utc, reloaded.SubmittedAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, reloaded.LastSavedAt!.Value.Kind);

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("x", "Python"))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submitting_after_the_grace_period_is_rejected()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        api.Time.Advance(TimeSpan.FromMinutes(31));
        student = await _world.LoginAsync(x.Student); // fresh token; the old one expired
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("x", "Python", null))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_student_deactivated_mid_exam_can_no_longer_write()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        await _world.WithDb(db => db.Users.Where(u => u.Id == x.Student.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("x", "Python"))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("x", "Python", null))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_exam_whose_questions_were_all_removed_cannot_be_started()
    {
        var x = await SetupAsync();
        await _world.WithDb(db => db.Questions.Where(q => q.ExamId == x.ExamId).ExecuteUpdateAsync(s => s.SetProperty(q => q.IsDeleted, true)));
        await (await Start(await _world.LoginAsync(x.Student), x.ExamId)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Evidence_is_validated_stored_and_served_only_to_the_right_people()
    {
        var x = await SetupAsync();
        var studentClient = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(studentClient, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        var face = await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("ReferenceFace", png, "image/png"))).ReadAsync<EvidenceDto>(HttpStatusCode.Created);
        var webcam = await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("Webcam", png, "image/png"))).ReadAsync<EvidenceDto>(HttpStatusCode.Created);
        await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("Webcam", png, "application/pdf"))).ValidationFieldsAsync();
        await (await studentClient.PostAsync($"/api/attempts/{attempt.Id}/evidence", Upload("Webcam", new byte[10 * 1024 * 1024 + 1], "image/png"))).ValidationFieldsAsync();

        var author = await _world.LoginAsync(x.Author);
        Assert.Equal(png, await (await author.GetAsync($"/api/evidence/{webcam.Id}")).Content.ReadAsByteArrayAsync());

        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, x.Section.Department));
        await (await colleague.GetAsync($"/api/evidence/{webcam.Id}")).ExpectProblemAsync(HttpStatusCode.NotFound);

        await (await studentClient.GetAsync($"/api/evidence/{face.Id}")).ExpectAsync(HttpStatusCode.OK);
        await (await studentClient.GetAsync($"/api/evidence/{webcam.Id}")).ExpectProblemAsync(HttpStatusCode.NotFound);

        static MultipartFormDataContent Upload(string kind, byte[] bytes, string contentType)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return new MultipartFormDataContent { { new StringContent(kind), "kind" }, { file, "file", "capture.bin" } };
        }
    }

    [Fact]
    public async Task Browser_audio_in_webm_is_accepted_as_evidence()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student, ClientKind.Web);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3 };
        var file = new ByteArrayContent(webm);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        var form = new MultipartFormDataContent { { new StringContent("Audio"), "kind" }, { file, "file", "chunk.webm" } };

        var evidence = await (await student.PostAsync($"/api/attempts/{attempt.Id}/evidence", form)).ReadAsync<EvidenceDto>(HttpStatusCode.Created);

        Assert.Equal("audio/webm", evidence.ContentType);
        var author = await _world.LoginAsync(x.Author);
        Assert.Equal(webm, await (await author.GetAsync($"/api/evidence/{evidence.Id}")).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Staff_with_ViewResults_list_attempts_and_watch_them_start_live()
    {
        var x = await SetupAsync();
        var authorLogin = await _world.LoginResponseAsync(x.Author);
        await using var live = await LiveClient.ConnectAsync(api, authorLogin.AccessToken);
        await live.JoinExamAsync(x.ExamId);

        var attempt = await (await Start(await _world.LoginAsync(x.Student), x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        await live.WaitForAsync(LiveMessages.AttemptStarted, m => m.AttemptId == attempt.Id);

        var list = await (await _world.Client(authorLogin.AccessToken).GetAsync($"/api/exams/{x.ExamId}/attempts")).ReadAsync<List<AttemptSummaryDto>>();
        Assert.Equal(attempt.Id, Assert.Single(list).Id);

        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, x.Section.Department));
        await (await colleague.GetAsync($"/api/exams/{x.ExamId}/attempts")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrent_strike_events_are_all_counted()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            student.PostJsonAsync($"/api/attempts/{attempt.Id}/events", new AttemptEventRequest(AttemptEventType.FocusLost, null, null))));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(8, (await _world.ReloadAttemptAsync(attempt.Id)).Strikes);
    }

    [Fact]
    public async Task Saving_after_submit_conflicts_and_keeps_the_submitted_code()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        await (await student.PostJsonAsync($"/api/attempts/{attempt.Id}/submit", new SubmitRequest("final", "Python", null))).ExpectAsync(HttpStatusCode.OK);

        await (await student.PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("stale", "Python"))).ExpectProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal("final", (await _world.ReloadAttemptAsync(attempt.Id)).Code);
    }

    [Fact]
    public async Task Evidence_missing_on_disk_is_a_404()
    {
        var x = await SetupAsync();
        var student = await _world.LoginAsync(x.Student);
        var attempt = await (await Start(student, x.ExamId)).ReadAsync<AttemptDto>(HttpStatusCode.Created);
        var id = await _world.WithDb(async db =>
        {
            var f = new EvidenceFile { AttemptId = attempt.Id, Kind = EvidenceKind.ReferenceFace, StoragePath = "nope/missing.png", ContentType = "image/png", SizeBytes = 1, CreatedAt = _world.Now };
            db.EvidenceFiles.Add(f);
            await db.SaveChangesAsync();
            return f.Id;
        });
        await (await student.GetAsync($"/api/evidence/{id}")).ExpectProblemAsync(HttpStatusCode.NotFound);
    }
}
