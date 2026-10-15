using Secucode.Contracts;

namespace Secucode.Desktop.Features.Student;

// One exam card on the student's list. Times are shown in local time.
public sealed record StudentExamRow(StudentExamDto Exam, DateTime NowUtc)
{
    public string Title => Exam.Title;
    public bool IsOpen => NowUtc >= Exam.StartsAt && NowUtc < Exam.EndsAt;
    public bool CanResume => Exam.InProgressAttemptId is not null;
    public bool CanStart => CanResume || IsOpen;

    public string ActionLabel =>
        CanResume ? "Resume"
        : IsOpen ? "Start"
        : NowUtc < Exam.StartsAt ? $"Opens {Local(Exam.StartsAt):d MMM, HH:mm}"
        : "Closed";

    public string When => $"{Local(Exam.StartsAt):ddd d MMM, HH:mm} – {Local(Exam.EndsAt):HH:mm} · {Exam.DurationMinutes} min";
    public string ModeText => Exam.Mode == ExamMode.DesktopOnly ? "Desktop only" : "Desktop or web";

    private static DateTime Local(DateTime utc) => utc.ToLocalTime();
}
