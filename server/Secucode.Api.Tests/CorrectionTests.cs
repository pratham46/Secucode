using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CorrectionTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private sealed record Setup(Section Section, TestUser Author, TestUser Student, Exam Exam);

    private async Task<Setup> SetupAsync(TimeSpan? startsIn = null, TimeSpan? window = null)
    {
        await _world.SetSwitchesAsync(true);
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch, startsIn: startsIn, window: window);
        return new Setup(s, author, student, exam);
    }

    [Fact]
    public async Task Reset_voids_the_attempt_keeps_its_data_and_lets_the_student_start_again()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);

        var voided = await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Webcam failed", null))).ReadAsync<AttemptDto>();

        Assert.Equal(AttemptStatus.Voided, voided.Status);
        var reloaded = await _world.ReloadAttemptAsync(attempt.Id);
        Assert.Equal(("print(1)", "Webcam failed"), (reloaded.Code, reloaded.VoidReason));
        var audit = await _world.LastAuditAsync();
        Assert.Equal(("attempt.reset", "Webcam failed", (int?)x.Exam.Id), (audit.Action, audit.Reason, audit.ExamId));

        var again = await (await (await _world.LoginAsync(x.Student)).PostJsonAsync($"/api/exams/{x.Exam.Id}/attempts", new StartAttemptRequest(World.Pin)))
            .ReadAsync<AttemptDto>(HttpStatusCode.Created);
        Assert.Equal(2, again.AttemptNumber);
    }

    [Fact]
    public async Task Resetting_after_the_window_needs_extra_minutes_which_become_the_new_time_allowance()
    {
        var x = await SetupAsync(startsIn: TimeSpan.FromHours(-3), window: TimeSpan.FromHours(1));
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);

        Assert.Equal(["extraMinutes"], await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Power cut", null))).ValidationFieldsAsync());
        await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Power cut", 20))).ExpectAsync(HttpStatusCode.OK);

        var restarted = await (await (await _world.LoginAsync(x.Student)).PostJsonAsync($"/api/exams/{x.Exam.Id}/attempts", new StartAttemptRequest(World.Pin)))
            .ReadAsync<AttemptDto>(HttpStatusCode.Created);
        Assert.Equal(_world.Now.AddMinutes(20), restarted.DeadlineAt, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Reopen_gives_a_submitted_attempt_more_time_and_keeps_the_code()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.AutoSubmitted);
        var author = await _world.LoginAsync(x.Author);

        var reopened = await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("Laptop crashed", 15))).ReadAsync<AttemptDto>();

        Assert.Equal(AttemptStatus.InProgress, reopened.Status);
        Assert.Equal("print(1)", reopened.Code);
        Assert.Equal(_world.Now.AddMinutes(15), reopened.DeadlineAt, TimeSpan.FromSeconds(2));
        await (await (await _world.LoginAsync(x.Student)).PutJsonAsync($"/api/attempts/{attempt.Id}/code", new SaveCodeRequest("print(2)", "Python")))
            .ExpectAsync(HttpStatusCode.NoContent);
        await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("Again", 5))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Extending_one_student_moves_their_deadline_and_tells_them_live()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id);
        await using var studentLive = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(x.Student)).AccessToken);
        var author = await _world.LoginAsync(x.Author);

        var extended = await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(10, "Power cut"))).ReadAsync<AttemptDto>();

        Assert.Equal(attempt.DeadlineAt.AddMinutes(10), extended.DeadlineAt, TimeSpan.FromSeconds(1));
        var msg = await studentLive.WaitForAsync(LiveMessages.DeadlineChanged, m => m.AttemptId == attempt.Id);
        Assert.Equal(extended.DeadlineAt, msg.DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal("attempt.extend", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Extending_the_whole_exam_moves_every_running_attempt_and_optionally_the_window()
    {
        var x = await SetupAsync();
        var a1 = await _world.AttemptAsync(x.Exam.Id, x.Student.Id);
        var a2 = await _world.AttemptAsync(x.Exam.Id, (await _world.UserAsync(RoleNames.Student, x.Section.Batch)).Id);
        var done = await _world.AttemptAsync(x.Exam.Id, (await _world.UserAsync(RoleNames.Student, x.Section.Batch)).Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);
        var before = await _world.AuditCountAsync();

        var result = await (await author.PostJsonAsync($"/api/exams/{x.Exam.Id}/extend", new ExtendExamRequest(15, "Fire drill", ExtendWindow: true))).ReadAsync<ExtendExamResult>();

        Assert.Equal(2, result.AttemptsExtended);
        Assert.Equal(x.Exam.EndsAt.AddMinutes(15), result.EndsAt, TimeSpan.FromSeconds(1));
        Assert.Equal(a1.DeadlineAt.AddMinutes(15), (await _world.ReloadAttemptAsync(a1.Id)).DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(a2.DeadlineAt.AddMinutes(15), (await _world.ReloadAttemptAsync(a2.Id)).DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(done.DeadlineAt, (await _world.ReloadAttemptAsync(done.Id)).DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(before + 1, await _world.AuditCountAsync());
        Assert.Equal("exam.extend", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Switches_turn_corrections_off_globally_or_for_one_exam()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var author = await _world.LoginAsync(x.Author);
        try
        {
            await _world.SetSwitchesAsync(false);
            Assert.Contains("whole institution",
                await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("x", null))).ExpectProblemAsync(HttpStatusCode.Forbidden));

            await _world.SetSwitchesAsync(true);
            await _world.WithDb(db => db.Exams.Where(e => e.Id == x.Exam.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.AllowAttemptReopen, false)));
            Assert.Contains("this exam",
                await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("x", 5))).ExpectProblemAsync(HttpStatusCode.Forbidden));
        }
        finally
        {
            await _world.SetSwitchesAsync(true);
        }
    }

    [Fact]
    public async Task Corrections_follow_the_capability_grid_even_for_the_exam_creator()
    {
        var x = await SetupAsync();
        var a1 = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, AttemptStatus.Submitted);
        var a2 = await _world.AttemptAsync(x.Exam.Id, (await _world.UserAsync(RoleNames.Student, x.Section.Batch)).Id, AttemptStatus.Submitted);
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, x.Section.Department));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, x.Section.Department));
        var author = await _world.LoginAsync(x.Author);
        var facultyRole = await _world.RoleIdAsync(RoleNames.Faculty);

        await (await colleague.PostJsonAsync($"/api/attempts/{a1.Id}/reset", new ResetAttemptRequest("x", null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.PostJsonAsync($"/api/attempts/{a1.Id}/reset", new ResetAttemptRequest("HOD approved", null))).ExpectAsync(HttpStatusCode.OK);

        try
        {
            await _world.WithDb(db => db.RoleCapabilities.Where(c => c.RoleId == facultyRole && c.Capability == Capability.ResetAttempts).ExecuteDeleteAsync());
            await (await author.PostJsonAsync($"/api/attempts/{a2.Id}/reset", new ResetAttemptRequest("x", null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        }
        finally
        {
            await _world.WithDb(async db =>
            {
                db.RoleCapabilities.Add(new RoleCapability { RoleId = facultyRole, Capability = Capability.ResetAttempts, Reach = Reach.OwnOnly });
                await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task Auto_close_respects_an_extension()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id, deadlineIn: TimeSpan.FromMinutes(1));
        await (await (await _world.LoginAsync(x.Author)).PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(10, "Late start")))
            .ExpectAsync(HttpStatusCode.OK);

        api.Time.Advance(TimeSpan.FromMinutes(3));
        await api.Factory.Services.GetRequiredService<AutoCloseService>().CloseExpiredAsync(CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(attempt.Id)).Status);
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var x = await SetupAsync();
        var attempt = await _world.AttemptAsync(x.Exam.Id, x.Student.Id);
        var author = await _world.LoginAsync(x.Author);
        Assert.Equal(["reason"], await (await author.PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(5, " "))).ValidationFieldsAsync());
    }
}
