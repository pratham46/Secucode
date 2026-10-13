namespace Secucode.Contracts;

public sealed record LiveAttemptMessage(int AttemptId, int ExamId, int StudentId, AttemptStatus Status, DateTime DeadlineAt, int Strikes,
    AttemptEventType? EventType);

// SignalR method names pushed by /hubs/exams.
public static class LiveMessages
{
    public const string AttemptStarted = "AttemptStarted";
    public const string EventRecorded = "EventRecorded";
    public const string CodeSaved = "CodeSaved";
    public const string AttemptSubmitted = "AttemptSubmitted";
    public const string DeadlineChanged = "DeadlineChanged";
    public const string AttemptReopened = "AttemptReopened";
    public const string AttemptVoided = "AttemptVoided";

    public static readonly string[] All = [AttemptStarted, EventRecorded, CodeSaved, AttemptSubmitted, DeadlineChanged, AttemptReopened, AttemptVoided];
}
