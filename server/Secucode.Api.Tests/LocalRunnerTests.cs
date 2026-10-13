using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

// Needs `python` and `node` on PATH (the test host runs the Local runner).
[Collection(ApiCollection.Name)]
public sealed class LocalRunnerTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<HttpClient> SomeoneAsync() =>
        await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department));

    private static async Task<RunResult> Run(HttpClient c, string language, string code, string? stdin = null) =>
        await (await c.PostJsonAsync("/api/run", new RunRequest(language, code, stdin))).ReadAsync<RunResult>();

    [Fact]
    public async Task Python_runs_and_reads_stdin()
    {
        var r = await Run(await SomeoneAsync(), "Python", "n=int(input()); print(sum(range(1,n+1)))", "100");
        Assert.Equal("5050", r.Stdout.Trim());
        Assert.Equal(0, r.ExitCode);
        Assert.False(r.TimedOut);
    }

    [Fact]
    public async Task JavaScript_runs()
    {
        Assert.Equal("42", (await Run(await SomeoneAsync(), "javascript", "console.log(6*7)")).Stdout.Trim());
    }

    [Fact]
    public async Task Runaway_programs_time_out()
    {
        Assert.True((await Run(await SomeoneAsync(), "Python", "while True: pass")).TimedOut);
    }

    [Fact]
    public async Task Output_is_capped()
    {
        var r = await Run(await SomeoneAsync(), "Python", "print('x' * 200000)");
        Assert.InRange(r.Stdout.Length, 1, 65536);
    }

    [Fact]
    public async Task Runtime_errors_are_reported()
    {
        var r = await Run(await SomeoneAsync(), "Python", "raise ValueError('boom')");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("ValueError", r.Stderr);
    }

    [Fact]
    public async Task Unread_stdin_cannot_outlive_the_time_limit()
    {
        var r = await Run(await SomeoneAsync(), "Python", "import time; time.sleep(30)", new string('x', 60_000));
        Assert.True(r.TimedOut);
    }

    [Fact]
    public async Task Child_processes_do_not_see_server_secrets()
    {
        Environment.SetEnvironmentVariable("Jwt__Key", "leak-check");
        try
        {
            var r = await Run(await SomeoneAsync(), "Python", "import os; print('Jwt__Key' in os.environ, 'ConnectionStrings__Default' in os.environ)");
            Assert.Equal("False False", r.Stdout.Trim());
        }
        finally { Environment.SetEnvironmentVariable("Jwt__Key", null); }
    }

    [Fact]
    public async Task Unsupported_languages_are_rejected()
    {
        var c = await SomeoneAsync();
        Assert.Equal(["language"], await (await c.PostJsonAsync("/api/run", new RunRequest("Cobol", "x", null))).ValidationFieldsAsync());
    }

    [Fact]
    public async Task Running_code_requires_sign_in()
    {
        await (await api.Factory.CreateClient().PostJsonAsync("/api/run", new RunRequest("Python", "print(1)", null)))
            .ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_user_gets_ten_runs_per_minute()
    {
        var c = await SomeoneAsync();
        for (var i = 0; i < 10; i++)
            await (await c.PostJsonAsync("/api/run", new RunRequest("Python", "print(1)", null))).ExpectAsync(HttpStatusCode.OK);
        await (await c.PostJsonAsync("/api/run", new RunRequest("Python", "print(1)", null))).ExpectProblemAsync(HttpStatusCode.TooManyRequests);
    }
}
