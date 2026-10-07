using System.Text;
using Microsoft.Extensions.Options;
using Secucode.Api.Common;
using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

public static class RunnerEndpoints
{
    public const string RateLimitPolicy = "run";

    public static void MapRunner(this IEndpointRouteBuilder api)
    {
        api.MapPost("/run", async (RunRequest req, ICodeRunner runner, IOptions<RunnerOptions> options, CancellationToken ct) =>
        {
            var o = options.Value;
            new Validator()
                .Rule(req.Language is not null && o.Languages.ContainsKey(req.Language), "language", $"Supported languages: {string.Join(", ", o.Languages.Keys)}.")
                .Rule(req.Code is not null && Encoding.UTF8.GetByteCount(req.Code) <= o.MaxCodeBytes, "code", $"Code is required and must be under {o.MaxCodeBytes / 1024} KB.")
                .Rule(Encoding.UTF8.GetByteCount(req.Stdin ?? "") <= o.MaxCodeBytes, "stdin", $"Input must be under {o.MaxCodeBytes / 1024} KB.")
                .ThrowIfInvalid();
            return await runner.RunAsync(req, ct);
        }).RequireRateLimiting(RateLimitPolicy);
    }
}
