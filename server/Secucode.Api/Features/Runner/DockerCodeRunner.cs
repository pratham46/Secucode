using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

// One container per run: no network, 256 MB, half a CPU, 64 processes, read-only root, user "nobody".
public sealed class DockerCodeRunner : ICodeRunner, IDisposable
{
    private readonly IOptions<RunnerOptions> _options;
    private readonly DockerClient _docker;
    private readonly ConcurrentDictionary<string, Task> _pulls = new();

    public DockerCodeRunner(IOptions<RunnerOptions> options)
    {
        _options = options;
        var endpoint = options.Value.DockerEndpoint;
        _docker = (endpoint is null ? new DockerClientConfiguration() : new DockerClientConfiguration(new Uri(endpoint))).CreateClient();
    }

    public async Task<RunResult> RunAsync(RunRequest request, CancellationToken ct)
    {
        var o = _options.Value;
        var lang = o.Languages[request.Language];
        await EnsureImageAsync(lang.Image);

        // Code and stdin travel as base64 env vars, so the root filesystem can stay read-only.
        var created = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = lang.Image,
            Cmd = ["sh", "-c", BuildScript(lang, o)],
            WorkingDir = "/work",
            User = "65534:65534",
            NetworkDisabled = true,
            Env =
            [
                $"CODE_B64={Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Code))}",
                $"STDIN_B64={Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Stdin ?? ""))}",
                "HOME=/work",
                "NO_COLOR=1",
            ],
            HostConfig = new HostConfig
            {
                NetworkMode = "none",
                Memory = 256L * 1024 * 1024,
                MemorySwap = 256L * 1024 * 1024,
                NanoCPUs = 500_000_000,
                PidsLimit = 64,
                ReadonlyRootfs = true,
                Tmpfs = new Dictionary<string, string>
                {
                    ["/work"] = "rw,exec,nosuid,size=64m,mode=1777",
                    ["/tmp"] = "rw,exec,nosuid,size=64m,mode=1777",
                },
            },
        }, ct);

        var clock = Stopwatch.StartNew();
        try
        {
            await _docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(o.CompileTimeoutSeconds + o.RunTimeoutSeconds + 5)); // backstop over the in-container timeouts
            long exitCode;
            var killed = false;
            try
            {
                exitCode = (await _docker.Containers.WaitContainerAsync(created.ID, cts.Token)).StatusCode;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                killed = true;
                exitCode = -1;
                try
                {
                    await _docker.Containers.KillContainerAsync(created.ID, new ContainerKillParameters(), CancellationToken.None);
                }
                catch (Exception e) when (e is DockerContainerNotFoundException or DockerApiException)
                {
                    // finished in the meantime; read its logs anyway
                }
            }

            var (stdout, stderr) = await ReadLogsAsync(created.ID, o.MaxOutputBytes);
            return new RunResult(stdout, stderr, (int)exitCode, killed || exitCode == 124 || (exitCode == 137 && clock.ElapsedMilliseconds >= o.RunTimeoutSeconds * 1000L), clock.ElapsedMilliseconds); // 124 = `timeout` fired, 137 = KILLed after -k (else an OOM kill)
        }
        finally
        {
            try
            {
                await _docker.Containers.RemoveContainerAsync(created.ID, new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            }
            catch
            {
                // best-effort cleanup must not mask the result or the original exception
            }
        }
    }

    public void Dispose() => _docker.Dispose();

    private static string BuildScript(LanguageOptions lang, RunnerOptions o)
    {
        var compile = lang.Compile is null ? "" : $"timeout -k 1 {o.CompileTimeoutSeconds} {lang.Compile} || exit $?; ";
        return $"echo \"$CODE_B64\" | base64 -d > {lang.FileName} && echo \"$STDIN_B64\" | base64 -d > /tmp/stdin.txt && "
             + $"{compile}timeout -k 1 {o.RunTimeoutSeconds} {lang.Run} < /tmp/stdin.txt > /tmp/out.txt 2> /tmp/err.txt; code=$?; "
             + $"head -c {o.MaxOutputBytes} /tmp/out.txt; head -c {o.MaxOutputBytes} /tmp/err.txt >&2; exit $code";
    }

    private async Task<(string Stdout, string Stderr)> ReadLogsAsync(string id, int max)
    {
        using var stream = await _docker.Containers.GetContainerLogsAsync(id, tty: false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, CancellationToken.None);
        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, CancellationToken.None);
            if (read.EOF)
                break;
            var target = read.Target == MultiplexedStream.TargetStream.StandardOut ? stdout : stderr;
            var room = max - (int)target.Length;
            if (room > 0)
                target.Write(buffer, 0, Math.Min(room, read.Count));
        }
        return (Encoding.UTF8.GetString(stdout.ToArray()), Encoding.UTF8.GetString(stderr.ToArray()));
    }

    // Pull each image once per process; a failed pull is retried on the next run.
    private async Task EnsureImageAsync(string image)
    {
        var pull = _pulls.GetOrAdd(image, PullIfMissingAsync);
        try
        {
            await pull;
        }
        catch
        {
            _pulls.TryRemove(image, out _);
            throw;
        }
    }

    private async Task PullIfMissingAsync(string image)
    {
        var existing = await _docker.Images.ListImagesAsync(new ImagesListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["reference"] = new Dictionary<string, bool> { [image] = true } },
        });
        if (existing.Count > 0)
            return;
        var parts = image.Split(':', 2);
        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = parts[0], Tag = parts.Length > 1 ? parts[1] : "latest" }, null, new Progress<JSONMessage>());
    }
}
