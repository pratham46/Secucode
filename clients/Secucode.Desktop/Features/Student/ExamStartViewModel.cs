using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Features.Student;

public sealed class ExamStartViewModel(ApiClient api, StudentExamDto exam)
{
    public StudentExamDto Exam => exam;
    public bool IsResume => exam.InProgressAttemptId is not null;

    // Start checks the PIN on the server; resume just reads the attempt that is already running.
    public async Task<AttemptDto> BeginAsync(string? pin, CancellationToken ct = default) =>
        exam.InProgressAttemptId is int id
            ? (await api.AttemptAsync(id, ct)).Attempt
            : await api.StartAttemptAsync(exam.Id, pin ?? "", ct);
}
