using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Desktop.Api;
using Secucode.Desktop.Features.Student;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ExamStartViewModelTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<(ApiClient Client, int ExamId)> StudentWithExamAsync()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var client = new ApiClient(api.Factory.CreateClient());
        await client.LoginAsync(student.Username, World.Password);
        return (client, exam.Id);
    }

    [Fact]
    public async Task Start_uses_the_pin_and_resume_returns_the_same_attempt_without_it()
    {
        var (client, examId) = await StudentWithExamAsync();
        var listed = (await client.StudentExamsAsync()).Single(e => e.Id == examId);

        var started = await new ExamStartViewModel(client, listed).BeginAsync(World.Pin);

        var again = (await client.StudentExamsAsync()).Single(e => e.Id == examId);
        var resume = new ExamStartViewModel(client, again);
        Assert.True(resume.IsResume);
        var resumed = await resume.BeginAsync(null);
        Assert.Equal(started.Id, resumed.Id);
        Assert.Equal(started.QuestionText, resumed.QuestionText);
    }

    [Fact]
    public async Task A_wrong_pin_shows_the_servers_reason()
    {
        var (client, examId) = await StudentWithExamAsync();
        var listed = (await client.StudentExamsAsync()).Single(e => e.Id == examId);

        var e = await Assert.ThrowsAsync<ApiError>(() => new ExamStartViewModel(client, listed).BeginAsync("0000"));

        Assert.Equal(403, e.Status);
        Assert.Equal("Incorrect PIN.", e.Detail);
    }
}
