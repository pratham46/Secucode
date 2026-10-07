using System.Collections.Concurrent;

namespace Secucode.Api.Auth;

// ponytail: in-memory, per API instance; move to the database or Redis if the API ever runs as several instances.
public sealed class LoginThrottle(TimeProvider time)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset? LockedUntil)> _state = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    // Serializes attempts per username so parallel guesses can't all slip past the lock check.
    // ponytail: per-instance, in-memory; gates are never evicted (one small semaphore per username tried).
    public async Task<IDisposable> AcquireAsync(string username)
    {
        var gate = _gates.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public bool IsLocked(string username) =>
        _state.TryGetValue(username, out var s) && s.LockedUntil is { } until && until > time.GetUtcNow();

    public void Fail(string username) => _state.AddOrUpdate(username,
        _ => (1, null),
        (_, s) =>
        {
            var now = time.GetUtcNow();
            var failures = s.LockedUntil is { } until && until <= now ? 1 : s.Failures + 1; // a served lock starts a fresh count
            return (failures, failures >= MaxFailures ? now + LockFor : null);
        });

    public void Succeed(string username) => _state.TryRemove(username, out _);
}
