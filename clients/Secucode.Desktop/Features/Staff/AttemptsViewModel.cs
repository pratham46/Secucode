using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Staff;

public sealed class AttemptsViewModel(ApiClient api, ExamDto exam)
{
    public ExamDto Exam => exam;
    public List<AttemptRow> Rows { get; private set; } = [];

    public async Task LoadAsync() => Rows = (await api.ExamAttemptsAsync(exam.Id)).Select(a => new AttemptRow(a)).ToList();

    public Task<AttemptDetailDto> DetailAsync(int attemptId) => api.AttemptAsync(attemptId);
}

public sealed record AttemptRow(AttemptSummaryDto Attempt)
{
    public string Student => Attempt.StudentName;
    public string Status => Attempt.Status switch
    {
        AttemptStatus.InProgress => "In progress",
        AttemptStatus.AutoSubmitted => "Auto-submitted",
        _ => Attempt.Status.ToString(),
    };
    public string Client => Attempt.Client.ToString();
    public string Started => Attempt.StartedAt.ToLocalTime().ToString("d MMM HH:mm");
    public string Submitted => Attempt.SubmittedAt?.ToLocalTime().ToString("d MMM HH:mm") ?? "—";
    public int Strikes => Attempt.Strikes;
}
