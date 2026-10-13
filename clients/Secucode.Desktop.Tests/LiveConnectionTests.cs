using Microsoft.AspNetCore.Http.Connections;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class LiveConnectionTests(ApiFixture api)
{
    private readonly World _world = new(api);

    [Fact]
    public async Task A_student_hears_their_deadline_change()
    {
        await _world.SetSwitchesAsync(true);
        var s = await _world.SectionAsync();
        var author = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var studentUser = await _world.UserAsync(RoleNames.Student, s.Batch);
        var exam = await _world.ExamAsync(author.Id, s.Batch);
        var attempt = await _world.AttemptAsync(exam.Id, studentUser.Id);
        var student = new ApiClient(api.Factory.CreateClient());
        await student.LoginAsync(studentUser.Username, World.Password);
        var server = api.Factory.Server;
        await using var live = new LiveConnection(server.BaseAddress, student.FreshAccessTokenAsync,
            () => server.CreateHandler(), HttpTransportType.LongPolling); // TestServer has no WebSockets
        var heard = new TaskCompletionSource<LiveAttemptMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = 0;
        live.Received += (method, m) => { if (method == LiveMessages.DeadlineChanged) heard.TrySetResult(m); };
        live.Connected += () => { connected++; return Task.CompletedTask; };

        await live.StartAsync();
        await (await _world.LoginAsync(author)).PostJsonAsync($"/api/attempts/{attempt.Id}/extend", new ExtendAttemptRequest(10, "Power cut"));

        var message = await heard.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(attempt.Id, message.AttemptId);
        Assert.Equal(attempt.DeadlineAt.AddMinutes(10), message.DeadlineAt, TimeSpan.FromSeconds(1));
        Assert.Equal(1, connected);
    }
}
