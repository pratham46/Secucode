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
}
