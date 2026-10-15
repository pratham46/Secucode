using Secucode.Contracts;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Proctoring;

namespace Secucode.Desktop.Features.Student;

// The exam's rules, free of UI and hardware: strike counting, submit-once, and reacting to the server.
// Every write goes through the outbox, so it survives network loss and crashes.
public sealed class ExamSession(AttemptDto attempt, AttemptOutbox outbox, Func<DateTime> utcNow)
{
    public const int MaxFaceStrikes = 3;
    public const int MaxFocusMisses = 3;

    public AttemptDto Attempt { get; private set; } = attempt;
    public ExamClock Clock { get; private set; } = new(attempt.DeadlineAt, attempt.ServerTime, utcNow());
    public int FaceStrikes { get; private set; }
    public int FocusMisses { get; private set; }
    public string? EndReason { get; private set; }
    public bool Ended => EndReason is not null;

    // The window should submit with this reason.
    public event Action<string>? AutoSubmitRequested;

    // The server closed the attempt (voided, or no longer in progress): stop without submitting.
    public event Action<string>? Closed;

    public void SaveCode(string code, string language)
    {
        if (!Ended) outbox.SaveCode(code, language);
    }

    public void FaceResult(FaceCheck result, byte[]? jpeg)
    {
        if (Ended || result == FaceCheck.NoFrame) return;
        if (result == FaceCheck.Ok)
        {
            FaceStrikes = 0; // strikes count consecutive failed checks
            return;
        }
        FaceStrikes++;
        outbox.Event(result switch
        {
            FaceCheck.Missing => AttemptEventType.FaceMissing,
            FaceCheck.Multiple => AttemptEventType.MultipleFaces,
            _ => AttemptEventType.FaceMismatch,
        }, null, jpeg);
        if (FaceStrikes >= MaxFaceStrikes)
            AutoSubmitRequested?.Invoke($"Face check failed {MaxFaceStrikes} times in a row ({result}).");
    }

    public void FocusLost()
    {
        if (Ended) return;
        FocusMisses++;
        outbox.Event(AttemptEventType.FocusLost, $"{FocusMisses}/{MaxFocusMisses}");
        if (FocusMisses >= MaxFocusMisses) AutoSubmitRequested?.Invoke("Test window lost focus too many times.");
    }

    public void Minimized()
    {
        if (Ended) return;
        outbox.Event(AttemptEventType.Minimized, null);
        AutoSubmitRequested?.Invoke("Test window was minimized.");
    }

    public void AudioUnavailable(string detail)
    {
        if (!Ended) outbox.Event(AttemptEventType.AudioUnavailable, detail.Length > 500 ? detail[..500] : detail);
    }

    // Queues the final evidence, then the submission. Only the first call counts.
    public bool Submit(string code, string language, string reason, byte[]? faceJpeg, byte[]? screenshotJpeg)
    {
        if (Ended) return false;
        EndReason = reason;
        if (faceJpeg is not null) outbox.Evidence(EvidenceKind.Webcam, faceJpeg, "image/jpeg");
        if (screenshotJpeg is not null) outbox.Evidence(EvidenceKind.Screenshot, screenshotJpeg, "image/jpeg");
        outbox.Submit(new SubmitRequest(code, language, reason));
        return true;
    }

    // Signed out mid-exam: nothing is submitted, and the attempt stays resumable.
    public void Abandon(string reason) => EndReason ??= reason;

    public void OnLive(string method, LiveAttemptMessage message)
    {
        if (message.AttemptId != Attempt.Id) return;
        if (method == LiveMessages.DeadlineChanged) Clock.DeadlineUtc = message.DeadlineAt;
        else if (method == LiveMessages.AttemptVoided) Close("This attempt was voided by staff.");
    }

    // After (re)connecting: pushes may have been missed, so take the server's view.
    public void Resync(AttemptDto fresh)
    {
        // A response fetched before a live extension landed must not shorten the clock again.
        if (fresh.Status == Attempt.Status && fresh.DeadlineAt < Clock.DeadlineUtc) return;
        Attempt = fresh;
        Clock = new ExamClock(fresh.DeadlineAt, fresh.ServerTime, utcNow());
        if (fresh.Status != AttemptStatus.InProgress) Close("This attempt is no longer in progress.");
    }

    private void Close(string reason)
    {
        if (Ended) return;
        EndReason = reason;
        Closed?.Invoke(reason);
    }
}
