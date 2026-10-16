using System.Net.Http;
using Secucode.Contracts;
using Secucode.Desktop.Api;
using Secucode.Desktop.Outbox;

namespace Secucode.Desktop.Tests;

public sealed class AttemptOutboxTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly string _root = Directory.CreateTempSubdirectory("secucode_outbox_").FullName;
    private readonly FakeSender _sender = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly List<AttemptOutbox> _open = [];

    private AttemptOutbox New()
    {
        var outbox = new AttemptOutbox(_root, 7, _sender, (t, _) => { lock (_delays) _delays.Add(t); return Task.CompletedTask; });
        _open.Add(outbox);
        return outbox;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var outbox in _open) await outbox.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Sends_in_order_and_links_the_frame_to_its_event()
    {
        var outbox = New();
        outbox.Event(AttemptEventType.FaceMissing, null, [1, 2, 3]);
        outbox.SaveCode("a", "Python");
        outbox.Evidence(EvidenceKind.Screenshot, [9, 9], "image/jpeg");
        outbox.Submit(new SubmitRequest("a", "Python", "Done"));

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["evidence:Webcam:3", "event:FaceMissing:101", "code:a", "evidence:Screenshot:2", "submit:a"], _sender.Calls);
    }

    [Fact]
    public async Task Only_the_latest_unsent_code_save_is_kept()
    {
        var outbox = New();
        outbox.SaveCode("a", "Python");
        outbox.SaveCode("b", "Python");
        outbox.SaveCode("c", "Python");

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:c"], _sender.Calls);
    }

    [Fact]
    public async Task Queue_survives_a_restart_and_sends_in_order()
    {
        const string code = "print('नमस्ते 🙂')";
        var crashed = New();
        crashed.Event(AttemptEventType.FocusLost, "1/3");
        crashed.SaveCode(code, "Python");
        await crashed.DisposeAsync(); // never started: nothing was sent before the "crash"
        _open.Remove(crashed);

        var restarted = New();
        Assert.Equal(2, restarted.Pending);
        restarted.Submit(new SubmitRequest(code, "Python", null));
        restarted.Start();

        Assert.True(await restarted.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["event:FocusLost:", $"code:{code}", $"submit:{code}"], _sender.Calls);
    }

    [Fact]
    public async Task Backs_off_while_offline_and_reports_it()
    {
        var outbox = New();
        var states = new List<bool>();
        outbox.OnlineChanged += online => { lock (states) states.Add(online); };
        for (var i = 0; i < 6; i++)
            _sender.FailNext("code:", ApiError.Network(new HttpRequestException("down")));
        outbox.SaveCode("a", "Python");

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal<double>([1, 2, 4, 8, 16, 30], _delays.Select(d => d.TotalSeconds));
        Assert.Equal([false, true], states);
        Assert.Equal(["code:a"], _sender.Calls);
    }

    [Fact]
    public async Task Evidence_is_uploaded_once_when_the_event_post_fails()
    {
        var outbox = New();
        _sender.FailNext("event:", ApiError.Network(new HttpRequestException("down")));
        outbox.Event(AttemptEventType.MultipleFaces, null, [1, 2]);

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["evidence:Webcam:2", "event:MultipleFaces:101"], _sender.Calls);
    }

    [Fact]
    public async Task A_closed_attempt_drops_the_queue_and_refuses_more()
    {
        var outbox = New();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        outbox.AttemptClosed += () => closed.TrySetResult();
        _sender.FailNext("code:", new ApiError(409, "Conflict", "This attempt is no longer in progress."));
        outbox.SaveCode("a", "Python");
        outbox.Evidence(EvidenceKind.Audio, [1], "audio/wav");

        outbox.Start();

        await closed.Task.WaitAsync(Wait);
        Assert.True(outbox.Closed);
        Assert.Equal(0, outbox.Pending);
        Assert.Empty(_sender.Calls);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "7")));
        outbox.SaveCode("b", "Python");
        Assert.Equal(0, outbox.Pending);
    }

    [Fact]
    public async Task A_rejected_item_is_dropped_and_the_queue_moves_on()
    {
        var outbox = New();
        _sender.FailNext("evidence:", new ApiError(400, "Bad Request", "File must be between 1 byte and 10 MB."));
        outbox.Evidence(EvidenceKind.Audio, [1], "audio/wav");
        outbox.SaveCode("a", "Python");

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:a"], _sender.Calls);
    }

    [Fact]
    public async Task A_throwing_online_listener_does_not_stop_the_queue()
    {
        var outbox = New();
        outbox.OnlineChanged += _ => throw new InvalidOperationException("boom");
        _sender.FailNext("code:", ApiError.Network(new HttpRequestException("down")));
        outbox.SaveCode("a", "Python");
        outbox.Submit(new SubmitRequest("a", "Python", null));

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:a", "submit:a"], _sender.Calls);
    }

    [Fact]
    public async Task Corrupt_and_temp_files_are_skipped_on_open()
    {
        var first = New();
        first.SaveCode("good", "Python");
        await first.DisposeAsync();
        _open.Remove(first);
        var dir = Path.Combine(_root, "7");
        File.WriteAllText(Path.Combine(dir, "0000000050.json"), "{ not json");
        File.WriteAllText(Path.Combine(dir, "0000000051.json"), "null");
        File.WriteAllText(Path.Combine(dir, "0000000052.json.tmp"), "half");

        var reopened = New();
        Assert.Equal(1, reopened.Pending);
        reopened.Start();

        Assert.True(await reopened.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:good"], _sender.Calls);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public async Task A_rejected_frame_still_posts_the_event_without_evidence()
    {
        var outbox = New();
        _sender.FailNext("evidence:", new ApiError(413, "Payload Too Large", "File must be between 1 byte and 10 MB."));
        outbox.Event(AttemptEventType.FaceMissing, null, [1, 2, 3]);

        outbox.Start();

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["event:FaceMissing:"], _sender.Calls);
    }

    [Fact]
    public async Task A_submit_queued_during_backoff_is_sent_promptly()
    {
        var outbox = new AttemptOutbox(_root, 7, _sender, (_, ct) => Task.Delay(Timeout.Infinite, ct));
        _open.Add(outbox);
        _sender.FailNext("code:", ApiError.Network(new HttpRequestException("down")));
        outbox.SaveCode("a", "Python");
        outbox.Start();
        while (outbox.IsOnline) await Task.Delay(10); // now waiting out the backoff

        outbox.Submit(new SubmitRequest("a", "Python", null));

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:a", "submit:a"], _sender.Calls);
    }

    // Fails the first code save only after the test releases it, so a Submit can land while the send is in flight.
    private sealed class GatedSender(FakeSender inner) : IAttemptSender
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _first = 1;

        public async Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _first, 0) == 1)
            {
                Entered.SetResult();
                await Release.Task;
                throw ApiError.Network(new HttpRequestException("down"));
            }
            await inner.SaveCodeAsync(attemptId, request, ct);
        }

        public Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct) =>
            inner.UploadEvidenceAsync(attemptId, kind, bytes, contentType, ct);
        public Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct) =>
            inner.RecordEventAsync(attemptId, request, ct);
        public Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct) =>
            inner.SubmitAsync(attemptId, request, ct);
    }

    [Fact]
    public async Task Submit_after_the_skip_path_does_not_touch_a_disposed_wait()
    {
        var gated = new GatedSender(_sender);
        var outbox = new AttemptOutbox(_root, 7, gated, (_, ct) => Task.Delay(Timeout.Infinite, ct));
        _open.Add(outbox);
        outbox.SaveCode("a", "Python");
        outbox.Start();
        await gated.Entered.Task.WaitAsync(Wait);
        outbox.Submit(new SubmitRequest("a", "Python", "first")); // queued while the send is in flight
        gated.Release.SetResult();                                 // that send fails -> skip path, no backoff
        while (_sender.Calls.Count == 0) await Task.Delay(10);

        outbox.Submit(new SubmitRequest("a", "Python", "second")); // must not throw

        Assert.True(await outbox.WaitUntilEmptyAsync(Wait));
        Assert.Equal(["code:a", "submit:a", "submit:a"], _sender.Calls);
    }

    [Fact]
    public async Task Sweep_deletes_only_folders_of_attempts_the_server_says_are_finished()
    {
        foreach (var name in new[] { "1", "2", "3", "4", "notes" }) Directory.CreateDirectory(Path.Combine(_root, name));
        var asked = new List<int>();

        await AttemptOutbox.SweepAsync(_root, keep: new HashSet<int> { 2 }, isFinished: id =>
        {
            asked.Add(id);
            return Task.FromResult(id == 1 ? true : id == 3 ? false : throw new ApiError(404, "Not found", "Not found"));
        });

        Assert.Equal([1, 3, 4], asked.Order()); // never asks about an in-progress attempt
        Assert.Equal(["2", "3", "4", "notes"], Directory.EnumerateDirectories(_root).Select(Path.GetFileName).Order());
    }
}
