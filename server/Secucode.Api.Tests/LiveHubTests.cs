using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Live;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class LiveHubTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private LiveNotifier Notifier => api.Factory.Services.GetRequiredService<LiveNotifier>();

    [Fact]
    public async Task Exam_staff_receive_live_messages_and_outsiders_cannot_join()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var outsider = await _world.UserAsync(RoleNames.Faculty, b.Department);
        var exam = await _world.ExamAsync(author.Id, a.Batch);

        await using var staff = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(author)).AccessToken);
        await staff.JoinExamAsync(exam.Id);
        await Notifier.AttemptStartedAsync(new Attempt { Id = 987654, ExamId = exam.Id, StudentId = 1, Status = AttemptStatus.InProgress, DeadlineAt = _world.Now });
        Assert.Equal(exam.Id, (await staff.WaitForAsync(LiveMessages.AttemptStarted, m => m.AttemptId == 987654)).ExamId);

        await using var other = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(outsider)).AccessToken);
        var ex = await Assert.ThrowsAsync<HubException>(() => other.JoinExamAsync(exam.Id));
        Assert.Contains("can't monitor", ex.Message);
    }

    [Fact]
    public async Task Students_receive_changes_to_their_own_attempts()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);

        await using var live = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(student)).AccessToken);
        await Notifier.DeadlineChangedAsync(new Attempt { Id = 123456, ExamId = exam.Id, StudentId = student.Id, Status = AttemptStatus.InProgress, DeadlineAt = _world.Now.AddMinutes(45) });

        var msg = await live.WaitForAsync(LiveMessages.DeadlineChanged, m => m.AttemptId == 123456);
        Assert.Equal(student.Id, msg.StudentId);
    }

    [Fact]
    public async Task The_hub_requires_a_token()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LiveClient.ConnectAsync(api, ""));
    }
}
