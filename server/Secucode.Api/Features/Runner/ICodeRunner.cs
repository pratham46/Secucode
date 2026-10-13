using Secucode.Contracts;

namespace Secucode.Api.Features.Runner;

public interface ICodeRunner
{
    // The request is already validated: Language is a configured key, sizes are within limits.
    Task<RunResult> RunAsync(RunRequest request, CancellationToken ct);
}
