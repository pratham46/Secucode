using Secucode.Desktop.Features.Student;

namespace Secucode.Desktop.Tests;

public sealed class ExamClockTests
{
    private static readonly DateTime Server = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_fast_pc_clock_does_not_shorten_the_exam()
    {
        var local = Server.AddMinutes(10); // the PC is 10 minutes fast
        var clock = new ExamClock(Server.AddMinutes(30), Server, local);

        Assert.Equal(TimeSpan.FromMinutes(30), clock.Remaining(local));
        Assert.Equal(TimeSpan.FromMinutes(20), clock.Remaining(local.AddMinutes(10)));
    }

    [Fact]
    public void Remaining_never_goes_negative()
    {
        var clock = new ExamClock(Server.AddMinutes(1), Server, Server);
        Assert.Equal(TimeSpan.Zero, clock.Remaining(Server.AddMinutes(5)));
    }

    [Fact]
    public void Submit_grace_is_thirty_seconds_after_the_deadline_in_server_time()
    {
        var local = Server.AddMinutes(-3); // the PC is 3 minutes slow
        var clock = new ExamClock(Server.AddMinutes(30), Server, local);

        Assert.False(clock.PastSubmitGrace(local.AddMinutes(30).AddSeconds(29)));
        Assert.True(clock.PastSubmitGrace(local.AddMinutes(30).AddSeconds(31)));
    }

    [Fact]
    public void A_moved_deadline_changes_the_countdown()
    {
        var clock = new ExamClock(Server.AddMinutes(30), Server, Server);
        clock.DeadlineUtc = Server.AddMinutes(40);
        Assert.Equal(TimeSpan.FromMinutes(40), clock.Remaining(Server));
    }
}
