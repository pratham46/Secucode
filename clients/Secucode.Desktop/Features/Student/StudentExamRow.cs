using Secucode.Contracts;

namespace Secucode.Desktop.Features.Student;

// One exam card on the student's list. Times are shown in local time. The PC clock may be off, so the label is
// only a hint and Start stays enabled a few minutes either side of the window; the server has the final say.
public sealed record StudentExamRow(StudentExamDto Exam, DateTime NowUtc)
{
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(5);

    public string Title => Exam.Title;
    public bool IsOpen => NowUtc >= Exam.StartsAt && NowUtc < Exam.EndsAt;
    public bool CanResume => Exam.InProgressAttemptId is not null;
    public bool CanStart => CanResume || (NowUtc >= Exam.StartsAt - ClockSlack && NowUtc <= Exam.EndsAt + ClockSlack);

    public string ActionLabel =>
        CanResume ? "Resume"
        : IsOpen ? "Start"
        : NowUtc < Exam.StartsAt ? $"Opens {Local(Exam.StartsAt):d MMM, HH:mm}"
        : "Closed";

    public string When => $"{Local(Exam.StartsAt):ddd d MMM, HH:mm} – {Local(Exam.EndsAt):HH:mm} · {Exam.DurationMinutes} min";
    public string ModeText => Exam.Mode == ExamMode.DesktopOnly ? "Desktop only" : "Desktop or web";

    private static DateTime Local(DateTime utc) => utc.ToLocalTime();
}
