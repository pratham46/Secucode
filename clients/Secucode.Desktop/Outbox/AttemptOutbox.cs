using System.IO;
using System.Text.Json;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Outbox;

// Disk-backed FIFO of one attempt's writes (spec §6). A single loop sends items in order. Network trouble:
// wait and retry the same item (1 s doubling to 30 s). 404/409: the attempt is closed, so the queue is dropped.
// Other 4xx: the server will never accept that item, so it is dropped and the queue moves on.
public sealed class AttemptOutbox : IAsyncDisposable
{
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly string _dir;
    private readonly int _attemptId;
    private readonly IAttemptSender _sender;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly List<OutboxItem> _items;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _nextSeq;
    private long? _inFlight;
    private volatile bool _online = true;
    private volatile bool _closed;

    public event Action<bool>? OnlineChanged;
    public event Action? AttemptClosed;

    public AttemptOutbox(string rootDir, int attemptId, IAttemptSender sender, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _dir = Path.Combine(rootDir, attemptId.ToString());
        Directory.CreateDirectory(_dir);
        _attemptId = attemptId;
        _sender = sender;
        _delay = delay ?? Task.Delay;
        _items = Directory.EnumerateFiles(_dir, "*.json")
            .Select(f => JsonSerializer.Deserialize<OutboxItem>(File.ReadAllText(f), Json.Options)!)
            .OrderBy(i => i.Seq)
            .ToList();
        _nextSeq = _items.Count == 0 ? 1 : _items[^1].Seq + 1;
    }

    public static string DefaultRoot => Path.Combine(AppSettings.DataDir, "outbox");

    public bool IsOnline => _online;
    public bool Closed => _closed;
    public int Pending { get { lock (_items) return _items.Count; } }
    public IReadOnlyList<OutboxItem> Snapshot() { lock (_items) return _items.ToList(); }

    // Only the newest unsent save matters, so older queued saves are dropped.
    public void SaveCode(string code, string language)
    {
        lock (_items)
        {
            foreach (var old in _items.Where(i => i.Kind == OutboxKind.SaveCode && i.Seq != _inFlight).ToList())
                RemoveLocked(old.Seq);
            AddLocked(seq => new OutboxItem(seq, OutboxKind.SaveCode) { Code = new SaveCodeRequest(code, language) }, null);
        }
    }

    public void Evidence(EvidenceKind kind, byte[] bytes, string contentType) =>
        Add(seq => new OutboxItem(seq, OutboxKind.Evidence) { Evidence = kind, ContentType = contentType }, bytes);

    public void Event(AttemptEventType type, string? detail, byte[]? webcamJpeg = null) =>
        Add(seq => new OutboxItem(seq, OutboxKind.Event)
        {
            EventType = type,
            Detail = detail,
            Evidence = webcamJpeg is null ? null : EvidenceKind.Webcam,
            ContentType = webcamJpeg is null ? null : "image/jpeg",
        }, webcamJpeg);

    public void Submit(SubmitRequest request) =>
        Add(seq => new OutboxItem(seq, OutboxKind.Submit) { Submit = request }, null);

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    // ponytail: polling wait; it runs at most once per exam.
    public async Task<bool> WaitUntilEmptyAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Pending > 0 && DateTime.UtcNow < until) await Task.Delay(100);
        return Pending == 0;
    }

    public void DeleteIfEmpty()
    {
        if (Pending == 0 && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null) await _loop;
    }

    private void Add(Func<long, OutboxItem> make, byte[]? bytes)
    {
        lock (_items) AddLocked(make, bytes);
    }

    private void AddLocked(Func<long, OutboxItem> make, byte[]? bytes)
    {
        if (_closed) return; // the server won't accept anything more for this attempt
        var item = make(_nextSeq++);
        if (bytes is not null) File.WriteAllBytes(BinPath(item.Seq), bytes);
        WriteJson(item); // written after the bytes: a loaded item always has them
        _items.Add(item);
        _wake.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = FirstBackoff;
        while (!ct.IsCancellationRequested)
        {
            OutboxItem? item;
            lock (_items)
            {
                item = _items.FirstOrDefault();
                _inFlight = item?.Seq;
            }
            if (item is null)
            {
                try { await _wake.WaitAsync(ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await SendAsync(item, ct);
                lock (_items) { _inFlight = null; RemoveLocked(item.Seq); }
                SetOnline(true);
                backoff = FirstBackoff;
            }
            catch (ApiError e) when (e.Status is 404 or 409)
            {
                lock (_items)
                {
                    _closed = true;
                    _inFlight = null;
                    foreach (var i in _items.ToList()) RemoveLocked(i.Seq);
                }
                SetOnline(true);
                AttemptClosed?.Invoke();
                return;
            }
            catch (ApiError e) when (e.Status is 400 or 403 or 413 or 415)
            {
                lock (_items) { _inFlight = null; RemoveLocked(item.Seq); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                lock (_items) _inFlight = null;
                SetOnline(false);
                try { await _delay(backoff, ct); }
                catch (OperationCanceledException) { return; }
                backoff = backoff * 2 > MaxBackoff ? MaxBackoff : backoff * 2;
            }
        }
    }

    private async Task SendAsync(OutboxItem item, CancellationToken ct)
    {
        switch (item.Kind)
        {
            case OutboxKind.SaveCode:
                await _sender.SaveCodeAsync(_attemptId, item.Code!, ct);
                break;
            case OutboxKind.Evidence:
                await _sender.UploadEvidenceAsync(_attemptId, item.Evidence!.Value, File.ReadAllBytes(BinPath(item.Seq)), item.ContentType!, ct);
                break;
            case OutboxKind.Event:
                var evidenceId = item.EvidenceId;
                if (item.Evidence is { } kind && evidenceId is null)
                {
                    evidenceId = await _sender.UploadEvidenceAsync(_attemptId, kind, File.ReadAllBytes(BinPath(item.Seq)), item.ContentType!, ct);
                    lock (_items) ReplaceLocked(item with { EvidenceId = evidenceId });
                }
                await _sender.RecordEventAsync(_attemptId, new AttemptEventRequest(item.EventType!.Value, item.Detail, evidenceId), ct);
                break;
            case OutboxKind.Submit:
                await _sender.SubmitAsync(_attemptId, item.Submit!, ct);
                break;
        }
    }

    private string JsonPath(long seq) => Path.Combine(_dir, $"{seq:D10}.json");
    private string BinPath(long seq) => Path.Combine(_dir, $"{seq:D10}.bin");

    private void WriteJson(OutboxItem item)
    {
        var path = JsonPath(item.Seq);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(item, Json.Options));
        File.Move(path + ".tmp", path, overwrite: true); // atomic replace: a crash never leaves half an item
    }

    private void ReplaceLocked(OutboxItem item)
    {
        var i = _items.FindIndex(x => x.Seq == item.Seq);
        if (i < 0) return;
        _items[i] = item;
        WriteJson(item);
    }

    private void RemoveLocked(long seq)
    {
        _items.RemoveAll(x => x.Seq == seq);
        File.Delete(JsonPath(seq));
        File.Delete(BinPath(seq));
    }

    private void SetOnline(bool online)
    {
        if (_online == online) return;
        _online = online;
        OnlineChanged?.Invoke(online);
    }
}
