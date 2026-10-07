using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

// Runs code as a child process of the API in a throwaway folder.
// ponytail: no isolation — dev only. Production uses DockerCodeRunner (Runner:Mode = Docker).
public sealed class LocalCodeRunner(IOptions<RunnerOptions> options) : ICodeRunner
{
    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)
    {
        var o = options.Value;
        var lang = o.Languages[request.Language];
        var dir = Directory.CreateTempSubdirectory("secucode_run_").FullName;
        var clock = Stopwatch.StartNew();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, lang.FileName), request.Code, ct);

            var compile = lang.LocalCompile ?? lang.Compile;
            if (compile is not null)
            {
                var c = await ExecAsync(compile, dir, "", TimeSpan.FromSeconds(o.CompileTimeoutSeconds), o.MaxOutputBytes, ct);
                if (c.TimedOut || c.ExitCode != 0)
                    return c with { DurationMs = clock.ElapsedMilliseconds };
            }

            var r = await ExecAsync(lang.LocalRun ?? lang.Run, dir, request.Stdin ?? "", TimeSpan.FromSeconds(o.RunTimeoutSeconds), o.MaxOutputBytes, ct);
            return r with { DurationMs = clock.ElapsedMilliseconds };
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* a just-killed exe can stay locked for a moment */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<RunResult> ExecAsync(string command, string dir, string stdin, TimeSpan timeout, int maxChars, CancellationToken ct)
    {
        var (file, args) = Split(command, dir);
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = dir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment.Remove("FORCE_COLOR");
        psi.Environment["NO_COLOR"] = "1";

        Process proc;
        try
        {
            proc = Process.Start(psi)!;
        }
        catch (Win32Exception)
        {
            return new RunResult("", $"'{Path.GetFileName(file)}' is not installed on the server.", -1, false, 0);
        }

        using (proc)
        {
            // Read both streams concurrently so a chatty program can't fill a pipe and deadlock.
            var stdoutTask = ReadCappedAsync(proc.StandardOutput, maxChars);
            var stderrTask = ReadCappedAsync(proc.StandardError, maxChars);
            try
            {
                await proc.StandardInput.WriteAsync(stdin);
                proc.StandardInput.Close(); // EOF so input() doesn't wait forever
            }
            catch (IOException) { /* exited without reading stdin */ }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var timedOut = false;
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync(CancellationToken.None);
            }
            return new RunResult(await stdoutTask, await stderrTask, timedOut ? -1 : proc.ExitCode, timedOut, 0);
        }
    }

    // Keeps draining after the cap so the child never blocks on a full pipe.
    private static async Task<string> ReadCappedAsync(StreamReader reader, int maxChars)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(buffer)) > 0)
            if (sb.Length < maxChars)
                sb.Append(buffer, 0, Math.Min(n, maxChars - sb.Length));
        return sb.ToString();
    }

    // "./main.exe" resolves inside the run folder; anything else is looked up on PATH.
    private static (string File, string Args) Split(string command, string dir)
    {
        var space = command.IndexOf(' ');
        var file = space < 0 ? command : command[..space];
        var args = space < 0 ? "" : command[(space + 1)..];
        if (file.StartsWith("./"))
            file = Path.Combine(dir, file[2..]);
        return (file, args);
    }
}
