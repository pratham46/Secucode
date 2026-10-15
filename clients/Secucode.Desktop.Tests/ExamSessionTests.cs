using Secucode.Contracts;
using Secucode.Desktop.Features.Student;
using Secucode.Desktop.Outbox;
using Secucode.Desktop.Proctoring;

namespace Secucode.Desktop.Tests;

public sealed class ExamSessionTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateTempSubdirectory("secucode_session_").FullName;
    private AttemptOutbox _outbox = null!;

    public Task InitializeAsync()
    {
        _outbox = new AttemptOutbox(_root, 3, new FakeSender()); // never started: tests inspect the queue
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _outbox.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    private static AttemptDto Attempt(AttemptStatus status = AttemptStatus.InProgress, DateTime? deadline = null) =>
        new(3, 1, 2, 1, "Reverse a string", status, ClientKind.Desktop, Now, deadline ?? Now.AddMinutes(30), null, "Python", "", 0, null, Now);

    private ExamSession New() => new(Attempt(), _outbox, () => Now);

    private List<string> Queued() => _outbox.Snapshot().Select(i => i.Kind switch
    {
        OutboxKind.Event => $"event:{i.EventType}",
        OutboxKind.Evidence => $"evidence:{i.Evidence}",
        _ => i.Kind.ToString(),
    }).ToList();

    [Fact]
    public void Three_failed_face_checks_in_a_row_request_one_auto_submit()
    {
        var session = New();
        var requests = new List<string>();
        session.AutoSubmitRequested += requests.Add;

        session.FaceResult(FaceCheck.Missing, [1]);
        session.FaceResult(FaceCheck.Ok, null);
        session.FaceResult(FaceCheck.Missing, [1]);
        session.FaceResult(FaceCheck.Multiple, [1]);
        Assert.Empty(requests);
        Assert.Equal(2, session.FaceStrikes);

        session.FaceResult(FaceCheck.Mismatch, [1]);

        Assert.StartsWith("Face check failed 3 times in a row", Assert.Single(requests));
        Assert.Equal(["event:FaceMissing", "event:FaceMissing", "event:MultipleFaces", "event:FaceMismatch"], Queued());
        Assert.All(_outbox.Snapshot(), i => Assert.Equal(EvidenceKind.Webcam, i.Evidence));
    }

    [Fact]
    public void A_dropped_frame_changes_nothing()
    {
        var session = New();
        session.FaceResult(FaceCheck.Missing, [1]);
        session.FaceResult(FaceCheck.NoFrame, null);
        Assert.Equal(1, session.FaceStrikes);
        Assert.Single(Queued());
    }

    [Fact]
    public void The_third_focus_loss_requests_auto_submit()
    {
        var session = New();
        var requests = new List<string>();
        session.AutoSubmitRequested += requests.Add;

        session.FocusLost();
        session.FocusLost();
        Assert.Empty(requests);
        session.FocusLost();

        Assert.Equal("Test window lost focus too many times.", Assert.Single(requests));
        Assert.Equal(["event:FocusLost", "event:FocusLost", "event:FocusLost"], Queued());
    }

    [Fact]
    public void Minimizing_requests_auto_submit_at_once()
    {
        var session = New();
        var requests = new List<string>();
        session.AutoSubmitRequested += requests.Add;

        session.Minimized();

        Assert.Equal("Test window was minimized.", Assert.Single(requests));
        Assert.Equal(["event:Minimized"], Queued());
    }

    [Fact]
    public void Submit_happens_once_with_final_evidence_first()
    {
        var session = New();

        Assert.True(session.Submit("print(1)", "Python", "Done", [1], [2]));
        Assert.False(session.Submit("print(2)", "Python", "Again", null, null));
        session.FaceResult(FaceCheck.Missing, [1]);
        session.SaveCode("late", "Python");

        Assert.True(session.Ended);
        Assert.Equal(["evidence:Webcam", "evidence:Screenshot", "Submit"], Queued());
        Assert.Equal(new SubmitRequest("print(1)", "Python", "Done"), _outbox.Snapshot()[^1].Submit);
    }

    [Fact]
    public void Abandon_never_submits()
    {
        var session = New();

        session.Abandon("Signed out");

        Assert.False(session.Submit("x", "Python", "Closed", null, null));
        Assert.True(session.Ended);
        Assert.Empty(Queued());
    }

    [Fact]
    public void Code_saved_before_abandoning_stays_queued_for_resume()
    {
        var session = New();

        session.SaveCode("latest", "Python");
        session.Abandon("Signed out");
        session.SaveCode("after", "Python");

        Assert.Equal(new SaveCodeRequest("latest", "Python"), Assert.Single(_outbox.Snapshot()).Code);
    }

    [Fact]
    public void A_live_deadline_change_moves_the_clock_for_this_attempt_only()
    {
        var session = New();

        session.OnLive(LiveMessages.DeadlineChanged, new LiveAttemptMessage(99, 1, 2, AttemptStatus.InProgress, Now.AddHours(5), 0, null));
        session.OnLive(LiveMessages.DeadlineChanged, new LiveAttemptMessage(3, 1, 2, AttemptStatus.InProgress, Now.AddMinutes(45), 0, null));

        Assert.Equal(TimeSpan.FromMinutes(45), session.Clock.Remaining(Now));
    }

    [Fact]
    public void A_voided_attempt_closes_without_submitting()
    {
        var session = New();
        string? closed = null;
        session.Closed += reason => closed = reason;

        session.OnLive(LiveMessages.AttemptVoided, new LiveAttemptMessage(3, 1, 2, AttemptStatus.Voided, Now, 0, null));

        Assert.Equal("This attempt was voided by staff.", closed);
        Assert.Empty(Queued());
    }

    [Fact]
    public void Resync_takes_the_servers_deadline_and_notices_a_closed_attempt()
    {
        var session = New();
        string? closed = null;
        session.Closed += reason => closed = reason;

        session.Resync(Attempt(deadline: Now.AddMinutes(50)));
        Assert.Equal(TimeSpan.FromMinutes(50), session.Clock.Remaining(Now));
        Assert.Null(closed);

        session.Resync(Attempt(AttemptStatus.AutoSubmitted));
        Assert.NotNull(closed);
    }

    [Fact]
    public void A_stale_resync_does_not_undo_a_time_extension()
    {
        var session = New();
        session.OnLive(LiveMessages.DeadlineChanged, new LiveAttemptMessage(3, 1, 2, AttemptStatus.InProgress, Now.AddMinutes(45), 0, null));

        session.Resync(Attempt(deadline: Now.AddMinutes(30))); // fetched before the extension landed

        Assert.Equal(TimeSpan.FromMinutes(45), session.Clock.Remaining(Now));
        Assert.False(session.Ended);
    }

    [Fact]
    public void Code_saves_are_queued_until_the_exam_ends()
    {
        var session = New();
        session.SaveCode("a", "Python");
        session.SaveCode("b", "Java");
        Assert.Equal(new SaveCodeRequest("b", "Java"), Assert.Single(_outbox.Snapshot()).Code);
    }
}
