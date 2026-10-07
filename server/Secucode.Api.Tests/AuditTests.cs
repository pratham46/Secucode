using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using static System.Net.HttpStatusCode;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuditTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private ExamRequest NewExam(Section s) => new(World.Unique("Audit Exam"), _world.Now.AddMinutes(-5), _world.Now.AddHours(2), 30,
        ExamMode.WebAllowed, true, s.BranchId, s.ClassId, s.BatchId, true, true, true, true, [new QuestionInput(null, "Q1")]);

    private static async Task<PageDto<AuditEntryDto>> Audit(HttpClient c, string query) =>
        await (await c.GetAsync($"/api/audit?{query}")).ReadAsync<PageDto<AuditEntryDto>>();

    [Fact]
    public async Task Each_viewer_sees_only_the_audit_entries_in_their_reach()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var mine = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var colleague = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var otherDept = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, b.Department));
        var hodA = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var principal = await _world.LoginAsync(await _world.UserAsync(RoleNames.Principal, Scope.Institution));
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, a.Batch));

        var myExam = (await (await mine.PostJsonAsync("/api/exams", NewExam(a))).ReadAsync<ExamDto>(Created)).Id;
        var colleagueExam = (await (await colleague.PostJsonAsync("/api/exams", NewExam(a))).ReadAsync<ExamDto>(Created)).Id;
        var otherExam = (await (await otherDept.PostJsonAsync("/api/exams", NewExam(b))).ReadAsync<ExamDto>(Created)).Id;
        var newUser = await (await hodA.PostJsonAsync("/api/users", new CreateUserRequest(World.Unique("aud"), "Secret1", "Dept Hire", null, null,
            [new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), a.BranchId, null, null, null, null)]))).ReadAsync<UserDto>(Created);

        Assert.Single((await Audit(mine, $"examId={myExam}")).Items);
        Assert.Empty((await Audit(mine, $"examId={colleagueExam}")).Items);      // OwnOnly
        Assert.Single((await Audit(hodA, $"examId={colleagueExam}")).Items);     // department scope
        Assert.Empty((await Audit(hodA, $"examId={otherExam}")).Items);
        Assert.Single((await Audit(hodA, $"entityType=User&entityId={newUser.Id}")).Items); // department-level entry without an exam
        Assert.Single((await Audit(principal, $"examId={otherExam}")).Items);    // institution scope
        await (await student.GetAsync("/api/audit")).ExpectProblemAsync(Forbidden);
    }

    [Fact]
    public async Task Department_viewer_still_sees_the_history_of_a_deleted_exam()
    {
        var a = await _world.SectionAsync();
        var author = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var exam = (await (await author.PostJsonAsync("/api/exams", NewExam(a))).ReadAsync<ExamDto>(Created)).Id;
        await (await author.DeleteAsync($"/api/exams/{exam}")).ExpectAsync(NoContent);

        var actions = (await Audit(hod, $"examId={exam}")).Items.Select(e => e.Action).ToList();
        Assert.Contains("exam.delete", actions);
        Assert.Contains("exam.create", actions);
    }

    [Fact]
    public async Task From_and_to_accept_UTC_strings_without_shifting()
    {
        var a = await _world.SectionAsync();
        var author = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, a.Department));
        var exam = (await (await author.PostJsonAsync("/api/exams", NewExam(a))).ReadAsync<ExamDto>(Created)).Id;
        var at = DateTime.SpecifyKind((await Audit(author, $"examId={exam}")).Items.Single().OccurredAt, DateTimeKind.Utc);
        static string Z(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        Assert.Single((await Audit(author, $"examId={exam}&from={Z(at.AddMinutes(-1))}&to={Z(at.AddMinutes(1))}")).Items);
        Assert.Empty((await Audit(author, $"examId={exam}&from={Z(at.AddMinutes(1))}")).Items);
        Assert.Empty((await Audit(author, $"examId={exam}&to={Z(at.AddMinutes(-1))}")).Items);
    }

    [Fact]
    public async Task Every_audited_change_writes_exactly_one_row_and_failed_changes_write_none()
    {
        await _world.SetSwitchesAsync(true);
        var s = await _world.SectionAsync();
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        var authorUser = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var author = await _world.LoginAsync(authorUser);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var facultyRole = await _world.RoleIdAsync(RoleNames.Faculty);
        var anonymous = api.Factory.CreateClient();

        async Task<JsonElement> Audited(string action, HttpStatusCode expected, Func<Task<HttpResponseMessage>> call)
        {
            var before = await _world.AuditCountAsync();
            var body = await (await call()).ExpectAsync(expected);
            Assert.True(before + 1 == await _world.AuditCountAsync(), $"{action} should write exactly one audit row");
            Assert.Equal(action, (await _world.LastAuditAsync()).Action);
            return body;
        }

        async Task NotAudited(HttpStatusCode expected, Func<Task<HttpResponseMessage>> call)
        {
            var before = await _world.AuditCountAsync();
            await (await call()).ExpectAsync(expected);
            Assert.Equal(before, await _world.AuditCountAsync());
        }

        static int Id(JsonElement body) => body.GetProperty("id").GetInt32();

        // Masters and settings
        var branch = Id(await Audited("branches.create", Created, () => admin.PostJsonAsync("/api/branches", new MasterRequest(World.Unique("B")))));
        await Audited("branches.update", OK, () => admin.PutJsonAsync($"/api/branches/{branch}", new MasterRequest(World.Unique("B"))));
        await Audited("branches.delete", NoContent, () => admin.DeleteAsync($"/api/branches/{branch}"));
        await Audited("settings.update", OK, () => admin.PutJsonAsync("/api/settings", new SettingsDto(true, true, true, true)));

        // Users, roles, assignments
        var user = Id(await Audited("user.create", Created, () => admin.PostJsonAsync("/api/users", new CreateUserRequest(World.Unique("aud"), "Secret1", "Audit Person", null, null,
            [new AssignmentRequest(facultyRole, s.BranchId, null, null, null, null)]))));
        await Audited("user.update", OK, () => admin.PutJsonAsync($"/api/users/{user}", new UpdateUserRequest("Renamed", null, true)));
        await Audited("user.reset_password", NoContent, () => admin.PostJsonAsync($"/api/users/{user}/reset-password", new ResetPasswordRequest("Another1")));
        await Audited("user.reset_pin", NoContent, () => admin.PostJsonAsync($"/api/users/{user}/reset-pin", new ResetPinRequest("2468")));
        var role = Id(await Audited("role.create", Created, () => admin.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Aud Role"), null, 150, ScopeLevels.Department))));
        await Audited("role.update", OK, () => admin.PutJsonAsync($"/api/roles/{role}", new RoleRequest(World.Unique("Aud Role"), null, 160, ScopeLevels.Department)));
        await Audited("role.grants", OK, () => admin.PutJsonAsync($"/api/roles/{role}/capabilities", new List<GrantDto> { new(Capability.ViewResults, Reach.InScope) }));
        var assignment = Id(await Audited("assignment.create", Created, () => admin.PostJsonAsync($"/api/users/{user}/assignments", new AssignmentRequest(role, s.BranchId, null, null, null, null))));
        await Audited("assignment.update", OK, () => admin.PutJsonAsync($"/api/assignments/{assignment}", new UpdateAssignmentRequest(null, _world.Today.AddDays(30))));
        await Audited("assignment.delete", NoContent, () => admin.DeleteAsync($"/api/assignments/{assignment}"));
        await Audited("role.delete", NoContent, () => admin.DeleteAsync($"/api/roles/{role}"));
        await Audited("user.deactivate", NoContent, () => admin.DeleteAsync($"/api/users/{user}"));

        // Exams and corrections
        var exam = Id(await Audited("exam.create", Created, () => author.PostJsonAsync("/api/exams", NewExam(s))));
        await Audited("exam.update", OK, () => author.PutJsonAsync($"/api/exams/{exam}", NewExam(s)));
        await Audited("exam.set_active", OK, () => author.PatchJsonAsync($"/api/exams/{exam}/active", new SetActiveRequest(true)));
        var attempt = await _world.AttemptAsync(exam, student.Id);
        await Audited("attempt.extend", OK, () => author.PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(5, "Power cut")));
        await Audited("exam.extend", OK, () => author.PostJsonAsync($"/api/exams/{exam}/extend", new ExtendExamRequest(5, "Fire drill", false)));
        await _world.WithDb(db => db.Attempts.Where(a => a.Id == attempt.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AttemptStatus.Submitted)));
        await Audited("attempt.reopen", OK, () => author.PostJsonAsync($"/api/attempts/{attempt.Id}/reopen", new ReopenAttemptRequest("Crash", 10)));
        await Audited("attempt.reset", OK, () => author.PostJsonAsync($"/api/attempts/{attempt.Id}/reset", new ResetAttemptRequest("Webcam failed", null)));
        var spare = Id(await Audited("exam.create", Created, () => author.PostJsonAsync("/api/exams", NewExam(s))));
        await Audited("exam.delete", NoContent, () => author.DeleteAsync($"/api/exams/{spare}"));

        // Logins
        await Audited("auth.login", OK, () => anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(authorUser.Username, World.Password, ClientKind.Web)));
        await Audited("auth.login_failed", Unauthorized, () => anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(authorUser.Username, "nope", ClientKind.Web)));

        // Rejected changes leave no trace
        await NotAudited(Forbidden, () => author.PostJsonAsync("/api/branches", new MasterRequest(World.Unique("Nope"))));
        await NotAudited(BadRequest, () => admin.PostJsonAsync("/api/users", new CreateUserRequest("x", "1", "", null, null, [])));
        await NotAudited(Conflict, () => author.DeleteAsync($"/api/exams/{exam}"));
    }
}
