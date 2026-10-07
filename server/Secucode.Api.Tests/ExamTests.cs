using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ExamTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private ExamRequest Request(Scope target, params string[] questions) => new(
        World.Unique("Midterm"), _world.Now.AddHours(1), _world.Now.AddHours(3), 60, ExamMode.DesktopOnly, true,
        target.BranchId!.Value, target.ClassId, target.BatchId, true, true, true, true,
        (questions.Length == 0 ? ["Reverse a string", "Sum of digits"] : questions).Select(q => new QuestionInput(null, q)).ToList());

    [Fact]
    public async Task Faculty_create_exams_inside_their_scope_only_and_creation_is_audited()
    {
        var mine = await _world.SectionAsync();
        var other = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, mine.Department));

        var exam = await (await faculty.PostJsonAsync("/api/exams", Request(mine.Batch))).ReadAsync<ExamDto>(HttpStatusCode.Created);
        Assert.Equal(2, exam.Questions!.Count);
        var audit = await _world.LastAuditAsync();
        Assert.Equal(("exam.create", exam.Id, mine.BranchId), (audit.Action, audit.ExamId, audit.BranchId));

        await (await faculty.PostJsonAsync("/api/exams", Request(other.Batch))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Coordinators_create_exams_for_their_year_only()
    {
        var s = await _world.SectionAsync();
        var otherYear = await _world.SectionAsync();
        var cc = await _world.LoginAsync(await _world.UserAsync(RoleNames.ClassCoordinator, s.Year));

        await (await cc.PostJsonAsync("/api/exams", Request(s.Batch))).ExpectAsync(HttpStatusCode.Created);
        await (await cc.PostJsonAsync("/api/exams", Request(s.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await cc.PostJsonAsync("/api/exams", Request(otherYear.Year))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_exams_report_their_bad_fields()
    {
        var s = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var bad = Request(s.Batch, "  ") with { Title = "", EndsAt = _world.Now, DurationMinutes = 0 };

        var fields = await (await faculty.PostJsonAsync("/api/exams", bad)).ValidationFieldsAsync();
        Assert.Equal(["durationMinutes", "endsAt", "questions", "title"], fields.OrderBy(f => f).ToArray());
    }

    [Fact]
    public async Task Times_without_a_zone_are_treated_as_utc()
    {
        var s = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var json = $$"""
            {"title":"Zone test","startsAt":"2030-01-01T10:00:00","endsAt":"2030-01-01T12:00:00","durationMinutes":60,
             "mode":"WebAllowed","isActive":true,"branchId":{{s.BranchId}},"classId":null,"batchId":null,
             "allowQuestionEditAfterAttempts":true,"allowAttemptReset":true,"allowAttemptReopen":true,"allowTimeExtension":true,
             "questions":[{"id":null,"text":"Q"}]}
            """;

        var body = await (await faculty.PostAsync("/api/exams", new StringContent(json, Encoding.UTF8, "application/json"))).ExpectAsync(HttpStatusCode.Created);
        Assert.Equal("2030-01-01T10:00:00Z", body.GetProperty("startsAt").GetString());
    }

    [Fact]
    public async Task Staff_see_exams_they_created_or_oversee()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var hodA = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var hodB = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, b.Department));
        var exam = await _world.ExamAsync(author.Id, a.Batch);

        Assert.Contains(await List(await _world.LoginAsync(author)), e => e.Id == exam.Id);
        Assert.Contains(await List(hodA), e => e.Id == exam.Id);
        Assert.DoesNotContain(await List(colleague), e => e.Id == exam.Id);
        Assert.DoesNotContain(await List(hodB), e => e.Id == exam.Id);

        var detail = await (await hodA.GetAsync($"/api/exams/{exam.Id}")).ReadAsync<ExamDto>();
        Assert.Equal(2, detail.Questions!.Count);
        await (await hodB.GetAsync($"/api/exams/{exam.Id}")).ExpectProblemAsync(HttpStatusCode.NotFound);

        static async Task<List<ExamDto>> List(HttpClient c) => await (await c.GetAsync("/api/exams")).ReadAsync<List<ExamDto>>();
    }

    [Fact]
    public async Task Students_see_active_exams_for_their_batch_without_questions()
    {
        var s = await _world.SectionAsync();
        var other = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));
        var forMe = await _world.ExamAsync(author.Id, s.Year);
        var inactive = await _world.ExamAsync(author.Id, s.Batch, configure: e => e.IsActive = false);
        var notMine = await _world.ExamAsync(author.Id, other.Batch);

        var list = await (await student.GetAsync("/api/exams")).ReadAsync<List<StudentExamDto>>();
        Assert.Contains(list, e => e.Id == forMe.Id);
        Assert.DoesNotContain(list, e => e.Id == inactive.Id || e.Id == notMine.Id);

        var body = await (await student.GetAsync($"/api/exams/{forMe.Id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.False(body.TryGetProperty("questions", out _));
    }

    [Fact]
    public async Task Only_the_creator_or_ManageExams_can_edit_and_edits_are_audited()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var subHod = await _world.LoginAsync(await _world.UserAsync(RoleNames.SubHod, s.Department));
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var edit = Request(s.Batch, "New Q") with { Title = "Renamed" };

        await (await colleague.PutJsonAsync($"/api/exams/{exam.Id}", edit)).ExpectProblemAsync(HttpStatusCode.Forbidden);
        var updated = await (await subHod.PutJsonAsync($"/api/exams/{exam.Id}", edit)).ReadAsync<ExamDto>();

        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(["New Q"], updated.Questions!.Select(q => q.Text));
        var audit = await _world.LastAuditAsync();
        Assert.Equal("exam.update", audit.Action);
        Assert.Contains("Question 1", audit.BeforeJson);
    }

    [Fact]
    public async Task Questions_can_change_after_attempts_only_with_the_switch_and_snapshots_never_change()
    {
        var s = await _world.SectionAsync();
        var authorUser = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var author = await _world.LoginAsync(authorUser);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(authorUser.Id, s.Batch, questions: 1);
        var attempt = await _world.AttemptAsync(exam.Id, student.Id);
        var original = (await (await author.GetAsync($"/api/exams/{exam.Id}")).ReadAsync<ExamDto>()).Questions!.Single();
        var replaceQuestion = Request(s.Batch, "Brand new question") with { Title = exam.Title };

        try
        {
            await _world.SetSwitchesAsync(false);
            var globalOff = await (await author.PutJsonAsync($"/api/exams/{exam.Id}", replaceQuestion)).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.Contains("whole institution", globalOff);

            // Non-question edits are still fine while the switch is off.
            var keepQuestions = replaceQuestion with { Title = "Retitled", Questions = [new QuestionInput(original.Id, original.Text)] };
            await (await author.PutJsonAsync($"/api/exams/{exam.Id}", keepQuestions)).ExpectAsync(HttpStatusCode.OK);

            await _world.SetSwitchesAsync(true);
            await _world.WithDb(db => db.Exams.Where(e => e.Id == exam.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.AllowQuestionEditAfterAttempts, false)));
            var examOff = await (await author.PutJsonAsync($"/api/exams/{exam.Id}", replaceQuestion with { AllowQuestionEditAfterAttempts = false })).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.Contains("this exam", examOff);

            await _world.WithDb(db => db.Exams.Where(e => e.Id == exam.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.AllowQuestionEditAfterAttempts, true)));
            var updated = await (await author.PutJsonAsync($"/api/exams/{exam.Id}", replaceQuestion)).ReadAsync<ExamDto>();
            Assert.Equal(["Brand new question"], updated.Questions!.Select(q => q.Text));

            var reloaded = await _world.ReloadAttemptAsync(attempt.Id);
            Assert.Equal(attempt.QuestionText, reloaded.QuestionText);
            var oldQuestionDeleted = await _world.WithDb(db => db.Questions.Where(q => q.Id == original.Id).Select(q => q.IsDeleted).SingleAsync());
            Assert.True(oldQuestionDeleted);
        }
        finally
        {
            await _world.SetSwitchesAsync(true);
        }
    }

    [Fact]
    public async Task Exams_with_attempts_cannot_be_deleted_but_can_be_deactivated()
    {
        var s = await _world.SectionAsync();
        var authorUser = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var author = await _world.LoginAsync(authorUser);
        var used = await _world.ExamAsync(authorUser.Id, s.Batch);
        var unused = await _world.ExamAsync(authorUser.Id, s.Batch);
        await _world.AttemptAsync(used.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id);

        await (await author.DeleteAsync($"/api/exams/{used.Id}")).ExpectProblemAsync(HttpStatusCode.Conflict);
        var off = await (await author.PatchJsonAsync($"/api/exams/{used.Id}/active", new SetActiveRequest(false))).ReadAsync<ExamDto>();
        Assert.False(off.IsActive);
        Assert.Equal("exam.set_active", (await _world.LastAuditAsync()).Action);

        await (await author.DeleteAsync($"/api/exams/{unused.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("exam.delete", (await _world.LastAuditAsync()).Action);
    }
}
