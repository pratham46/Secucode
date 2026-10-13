using Microsoft.AspNetCore.SignalR;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Live;

// Call only after SaveChanges succeeds, so clients never hear about changes that rolled back.
public sealed class LiveNotifier(IHubContext<ExamHub> hub)
{
    public Task AttemptStartedAsync(Attempt a) => ToExam(LiveMessages.AttemptStarted, a, null);
    public Task EventRecordedAsync(Attempt a, AttemptEventType type) => ToExam(LiveMessages.EventRecorded, a, type);
    public Task CodeSavedAsync(Attempt a) => ToExam(LiveMessages.CodeSaved, a, null);
    public Task AttemptSubmittedAsync(Attempt a) => ToExam(LiveMessages.AttemptSubmitted, a, null);
    public Task DeadlineChangedAsync(Attempt a) => ToExamAndStudent(LiveMessages.DeadlineChanged, a);
    public Task AttemptReopenedAsync(Attempt a) => ToExamAndStudent(LiveMessages.AttemptReopened, a);
    public Task AttemptVoidedAsync(Attempt a) => ToExamAndStudent(LiveMessages.AttemptVoided, a);

    private static LiveAttemptMessage Message(Attempt a, AttemptEventType? type) =>
        new(a.Id, a.ExamId, a.StudentId, a.Status, a.DeadlineAt, a.Strikes, type);

    private Task ToExam(string method, Attempt a, AttemptEventType? type) =>
        hub.Clients.Group(LiveGroups.Exam(a.ExamId)).SendAsync(method, Message(a, type));

    private Task ToExamAndStudent(string method, Attempt a) =>
        hub.Clients.Groups(LiveGroups.Exam(a.ExamId), LiveGroups.User(a.StudentId)).SendAsync(method, Message(a, null));
}
