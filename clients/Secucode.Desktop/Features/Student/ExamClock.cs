namespace Secucode.Desktop.Features.Student;

// The server's clock is authoritative: the offset between server and PC is measured when the attempt is
// fetched, and every countdown uses server time.
public sealed class ExamClock(DateTime deadlineUtc, DateTime serverTimeUtc, DateTime localUtcAtFetch)
{
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _offset = serverTimeUtc - localUtcAtFetch;

    public DateTime DeadlineUtc { get; set; } = deadlineUtc;

    public DateTime ServerNow(DateTime localUtcNow) => localUtcNow + _offset;

    public TimeSpan Remaining(DateTime localUtcNow)
    {
        var left = DeadlineUtc - ServerNow(localUtcNow);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public bool PastSubmitGrace(DateTime localUtcNow) => ServerNow(localUtcNow) > DeadlineUtc + SubmitGrace;
}
