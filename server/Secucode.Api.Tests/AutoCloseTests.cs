using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

// The hosted loop is disabled in tests (Attempts:AutoCloseEnabled=false); tests call the sweep directly.
[Collection(ApiCollection.Name)]
public sealed class AutoCloseTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private AutoCloseService Service => api.Factory.Services.GetRequiredService<AutoCloseService>();

    [Fact]
    public async Task Expired_attempts_are_auto_submitted_with_their_last_saved_code()
    {
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var expiring = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(1));
        var running = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(60));
        var done = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, AttemptStatus.Submitted, deadlineIn: TimeSpan.FromMinutes(-10));
        await using var live = await LiveClient.ConnectAsync(api, (await _world.LoginResponseAsync(author)).AccessToken);
        await live.JoinExamAsync(exam.Id);

        api.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(31));
        Assert.True(await Service.CloseExpiredAsync(CancellationToken.None) >= 1);

        var closed = await _world.ReloadAttemptAsync(expiring.Id);
        Assert.Equal(AttemptStatus.AutoSubmitted, closed.Status);
        Assert.Equal("Deadline passed", closed.SubmitReason);
        Assert.Equal("print(1)", closed.Code);
        Assert.NotNull(closed.SubmittedAt);
        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(running.Id)).Status);
        Assert.Equal(AttemptStatus.Submitted, (await _world.ReloadAttemptAsync(done.Id)).Status);
        await live.WaitForAsync(LiveMessages.AttemptSubmitted, m => m.AttemptId == expiring.Id);
    }

    [Fact]
    public async Task Attempts_inside_the_grace_period_stay_open()
    {
        var s = await _world.SectionAsync();
        var exam = await _world.ExamAsync((await _world.UserAsync(RoleNames.Faculty, s.Department)).Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(1));

        api.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(10));
        await Service.CloseExpiredAsync(CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(attempt.Id)).Status);
    }

    [Fact]
    public async Task The_current_deadline_is_what_counts()
    {
        var s = await _world.SectionAsync();
        var exam = await _world.ExamAsync((await _world.UserAsync(RoleNames.Faculty, s.Department)).Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, (await _world.UserAsync(RoleNames.Student, s.Batch)).Id, deadlineIn: TimeSpan.FromMinutes(1));
        await _world.WithDb(db => db.Attempts.Where(a => a.Id == attempt.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.DeadlineAt, a => a.DeadlineAt.AddMinutes(30))));

        api.Time.Advance(TimeSpan.FromMinutes(5));
        await Service.CloseExpiredAsync(CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, (await _world.ReloadAttemptAsync(attempt.Id)).Status);
    }
}
