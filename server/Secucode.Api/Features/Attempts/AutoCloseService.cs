using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Features.Live;
using Secucode.Contracts;

namespace Secucode.Api.Features.Attempts;

public sealed class AttemptOptions
{
    public bool AutoCloseEnabled { get; set; } = true;
    public int AutoCloseIntervalSeconds { get; set; } = 15;
}

// A crashed laptop or lost network can't leave an attempt open forever (spec §8 step 4).
public sealed class AutoCloseService(IServiceScopeFactory scopes, TimeProvider time, IOptions<AttemptOptions> options,
    LiveNotifier live, ILogger<AutoCloseService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoCloseEnabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.AutoCloseIntervalSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CloseExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Auto-closing expired attempts failed; will retry on the next tick.");
            }
        }
    }

    public async Task<int> CloseExpiredAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.UtcNow();
        var cutoff = now - AttemptEndpoints.SubmitGrace;

        var expired = await db.Attempts.AsNoTracking().Where(a => a.Status == AttemptStatus.InProgress && a.DeadlineAt < cutoff).ToListAsync(ct);
        var closed = 0;
        foreach (var a in expired)
        {
            // Status and deadline guards in the WHERE: a concurrent submit or a just-committed extension wins (spec 8.6).
            var rows = await db.Attempts.Where(x => x.Id == a.Id && x.Status == AttemptStatus.InProgress && x.DeadlineAt < cutoff)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.Status, AttemptStatus.AutoSubmitted)
                    .SetProperty(y => y.SubmittedAt, now)
                    .SetProperty(y => y.SubmitReason, "Deadline passed"), ct);
            if (rows == 0)
                continue;
            a.Status = AttemptStatus.AutoSubmitted;
            a.SubmittedAt = now;
            a.SubmitReason = "Deadline passed";
            await live.AttemptSubmittedAsync(a);
            closed++;
        }
        return closed;
    }
}
