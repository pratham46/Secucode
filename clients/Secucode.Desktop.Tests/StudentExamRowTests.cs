using Secucode.Contracts;
using Secucode.Desktop.Features.Student;

namespace Secucode.Desktop.Tests;

public sealed class StudentExamRowTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

    private static StudentExamRow Row(int startsInMinutes, int? inProgress = null, int windowMinutes = 120) =>
        new(new StudentExamDto(1, "DSA mid-term", Now.AddMinutes(startsInMinutes), Now.AddMinutes(startsInMinutes + windowMinutes), 30,
            ExamMode.DesktopOnly, inProgress), Now);

    [Fact]
    public void An_open_exam_can_be_started()
    {
        var row = Row(-5);
        Assert.True(row.CanStart);
        Assert.Equal("Start", row.ActionLabel);
    }

    [Fact]
    public void An_attempt_in_progress_is_resumed_even_after_the_window_closed()
    {
        var row = Row(-200, inProgress: 42);
        Assert.True(row.CanStart);
        Assert.Equal("Resume", row.ActionLabel);
    }

    [Fact]
    public void Future_and_past_exams_cannot_be_started()
    {
        Assert.False(Row(30).CanStart);
        Assert.StartsWith("Opens ", Row(30).ActionLabel);
        Assert.False(Row(-300).CanStart);
        Assert.Equal("Closed", Row(-300).ActionLabel);
    }

    [Fact]
    public void Details_read_naturally()
    {
        var row = Row(-5);
        Assert.EndsWith("· 30 min", row.When);
        Assert.Equal("Desktop only", row.ModeText);
    }
}
