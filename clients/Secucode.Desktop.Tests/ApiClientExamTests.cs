using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ApiClientExamTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<ApiClient> SignInAsync(TestUser user)
    {
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(user.Username, World.Password);
        return client;
    }

    private async Task<(Section S, TestUser Author, TestUser Student)> PeopleAsync()
    {
        var s = await _world.SectionAsync();
        return (s, await _world.UserAsync(RoleNames.Faculty, s.Department), await _world.UserAsync(RoleNames.Student, s.Batch));
    }

    [Fact]
    public async Task A_student_takes_a_desktop_only_exam_end_to_end()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch, ExamMode.DesktopOnly);
        var student = await SignInAsync(studentUser);

        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);
        await student.SaveCodeAsync(attempt.Id, new SaveCodeRequest("print(1)", "Python"));
        var evidence = await student.UploadEvidenceAsync(attempt.Id, EvidenceKind.Webcam, [0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg");
        await student.RecordEventAsync(attempt.Id, new AttemptEventRequest(AttemptEventType.FaceMissing, null, evidence.Id));
        var submitted = await student.SubmitAsync(attempt.Id, new SubmitRequest("print(2)", "Python", "Done"));

        Assert.Equal(AttemptStatus.Submitted, submitted.Status);
        var detail = await student.AttemptAsync(attempt.Id);
        Assert.Equal("print(2)", detail.Attempt.Code);
        Assert.Equal(evidence.Id, Assert.Single(detail.Events).EvidenceId);
        Assert.Equal(EvidenceKind.Webcam, Assert.Single(detail.Evidence).Kind);
    }

    [Fact]
    public async Task An_attempt_in_progress_is_offered_for_resume()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await SignInAsync(studentUser);

        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);

        var listed = Assert.Single(await student.StudentExamsAsync(), e => e.Id == exam.Id);
        Assert.Equal(attempt.Id, listed.InProgressAttemptId);
    }

    [Fact]
    public async Task Writes_after_submit_are_409()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await SignInAsync(studentUser);
        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);
        await student.SubmitAsync(attempt.Id, new SubmitRequest("x", "Python", null));

        var e = await Assert.ThrowsAsync<ApiError>(() => student.SaveCodeAsync(attempt.Id, new SaveCodeRequest("y", "Python")));

        Assert.Equal(409, e.Status);
    }

    [Fact]
    public async Task Code_round_trips_unicode()
    {
        var (s, author, studentUser) = await PeopleAsync();
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var student = await SignInAsync(studentUser);
        var attempt = await student.StartAttemptAsync(exam.Id, World.Pin);
        const string code = "print('नमस्ते 🙂')";

        await student.SaveCodeAsync(attempt.Id, new SaveCodeRequest(code, "Python"));

        Assert.Equal(code, (await student.AttemptAsync(attempt.Id)).Attempt.Code);
    }

    [Fact]
    public async Task Staff_create_list_toggle_and_see_attempts()
    {
        var (s, authorUser, studentUser) = await PeopleAsync();
        var author = await SignInAsync(authorUser);
        var start = DateTime.UtcNow.AddMinutes(-5);
        var request = new ExamRequest(World.Unique("Exam"), start, start.AddHours(2), 30, ExamMode.DesktopOnly, true,
            s.BranchId, s.ClassId, s.BatchId, true, true, true, true, [new QuestionInput(null, "Reverse a string")]);

        var created = await author.CreateExamAsync(request);
        await author.SetExamActiveAsync(created.Id, false);
        await author.SetExamActiveAsync(created.Id, true);
        var student = await SignInAsync(studentUser);
        await student.StartAttemptAsync(created.Id, World.Pin);

        Assert.Contains(await author.StaffExamsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)), e => e.Id == created.Id);
        Assert.Equal("Reverse a string", Assert.Single((await author.ExamAsync(created.Id)).Questions!).Text);
        Assert.Equal(studentUser.Id, Assert.Single(await author.ExamAttemptsAsync(created.Id)).StudentId);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiError>(() => author.DeleteExamAsync(created.Id))).Status);
    }

    [Fact]
    public async Task Invalid_exam_shows_field_errors()
    {
        var (s, authorUser, _) = await PeopleAsync();
        var author = await SignInAsync(authorUser);
        var bad = new ExamRequest("", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1), 0, ExamMode.DesktopOnly, true,
            s.BranchId, null, null, true, true, true, true, []);

        var e = await Assert.ThrowsAsync<ApiError>(() => author.CreateExamAsync(bad));

        Assert.Equal(400, e.Status);
        Assert.NotEmpty(e.Errors);
    }

    [Fact]
    public async Task Run_and_masters()
    {
        var (s, authorUser, _) = await PeopleAsync();
        var author = await SignInAsync(authorUser);

        var run = await author.RunAsync(new RunRequest("Python", "print('hi')", null));

        Assert.Equal("hi", run.Stdout.Trim());
        Assert.Contains(await author.MastersAsync("branches"), b => b.Id == s.BranchId);
        Assert.Contains(await author.MastersAsync("batches"), b => b.Id == s.BatchId);
    }
}
