using Secucode.Contracts;
using Secucode.Desktop.Outbox;

namespace Secucode.Desktop.Tests;

// Records calls as short strings; FailNext makes the next call starting with a prefix throw.
public sealed class FakeSender : IAttemptSender
{
    private readonly List<(string Prefix, Exception Error)> _failures = [];
    private long _nextEvidenceId = 100;

    public List<string> Calls { get; } = [];

    public void FailNext(string prefix, Exception error)
    {
        lock (Calls) _failures.Add((prefix, error));
    }

    private void Hit(string call)
    {
        lock (Calls)
        {
            var i = _failures.FindIndex(f => call.StartsWith(f.Prefix, StringComparison.Ordinal));
            if (i >= 0)
            {
                var error = _failures[i].Error;
                _failures.RemoveAt(i);
                throw error;
            }
            Calls.Add(call);
        }
    }

    public Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct)
    {
        Hit($"code:{request.Code}");
        return Task.CompletedTask;
    }

    public Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct)
    {
        Hit($"evidence:{kind}:{bytes.Length}");
        return Task.FromResult(Interlocked.Increment(ref _nextEvidenceId));
    }

    public Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct)
    {
        Hit($"event:{request.Type}:{request.EvidenceId}");
        return Task.CompletedTask;
    }

    public Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct)
    {
        Hit($"submit:{request.Code}");
        return Task.CompletedTask;
    }
}
