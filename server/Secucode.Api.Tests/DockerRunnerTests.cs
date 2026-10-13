using Microsoft.Extensions.Options;
using Secucode.Api.Features.Runner;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

// Pulls python:3.12-slim and gcc:14 on first run (gcc is ~1 GB), so the first run is slow.
[Trait("Category", "Docker")]
public sealed class DockerRunnerTests : IDisposable
{
    private readonly DockerCodeRunner _runner = new(Options.Create(new RunnerOptions { Mode = "Docker" }));

    public void Dispose() => _runner.Dispose();

    private Task<RunResult> Run(string language, string code, string? stdin = null) =>
        _runner.RunAsync(new RunRequest(language, code, stdin), CancellationToken.None);

    [Fact]
    public async Task Python_runs_with_stdin_as_an_unprivileged_user()
    {
        var r = await Run("Python", "import os\nn=int(input())\nprint(n*2, os.getuid())", "21");
        Assert.Equal("42 65534", r.Stdout.Trim());
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Cpp_compiles_and_runs_and_compile_errors_are_reported()
    {
        Assert.Equal("ok", (await Run("C++", "#include <iostream>\nint main(){std::cout<<\"ok\";}")).Stdout);

        var bad = await Run("C++", "int main( { }");
        Assert.NotEqual(0, bad.ExitCode);
        Assert.Contains("error", bad.Stderr);
    }

    [Fact]
    public async Task Runaway_programs_time_out()
    {
        Assert.True((await Run("Python", "while True: pass")).TimedOut);
    }

    [Fact]
    public async Task Endless_output_is_bounded()
    {
        var r = await Run("Python", "while True: print('x' * 1000)");
        Assert.NotEqual(0, r.ExitCode); // /tmp fills up (64m) and the write fails, which ends the program before the timeout
        Assert.True(r.Stdout.Length <= 65536);
    }

    [Fact]
    public async Task There_is_no_network()
    {
        var r = await Run("Python", "import urllib.request\nurllib.request.urlopen('http://1.1.1.1', timeout=3)");
        Assert.NotEqual(0, r.ExitCode);
    }

    [Fact]
    public async Task Memory_is_limited()
    {
        var r = await Run("Python", "x = b'x' * (1024 * 1024 * 1024)\nprint('allocated')"); // writes every byte, so the limit really bites
        Assert.NotEqual(0, r.ExitCode);
        Assert.DoesNotContain("allocated", r.Stdout);
    }
}
