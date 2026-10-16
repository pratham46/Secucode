using Secucode.Contracts;

namespace Secucode.Desktop.Outbox;

public enum OutboxKind { SaveCode, Evidence, Event, Submit }

// One queued write, stored as "{Seq}.json" plus "{Seq}.bin" for evidence bytes. For an Event with a webcam frame,
// EvidenceId is written back once the upload succeeds, so a retry (even after a crash) never uploads it twice.
public sealed record OutboxItem(long Seq, OutboxKind Kind)
{
    public SaveCodeRequest? Code { get; init; }
    public SubmitRequest? Submit { get; init; }
    public AttemptEventType? EventType { get; init; }
    public string? Detail { get; init; }
    public EvidenceKind? Evidence { get; init; }
    public string? ContentType { get; init; }
    public long? EvidenceId { get; init; }
}

public interface IAttemptSender
{
    Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct);
    Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct);
    Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct);
    Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct);
}
