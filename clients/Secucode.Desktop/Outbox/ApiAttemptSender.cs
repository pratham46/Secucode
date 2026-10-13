using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Outbox;

public sealed class ApiAttemptSender(ApiClient api) : IAttemptSender
{
    public Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct) =>
        api.SaveCodeAsync(attemptId, request, ct);

    public async Task<long> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct) =>
        (await api.UploadEvidenceAsync(attemptId, kind, bytes, contentType, ct)).Id;

    public Task RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct) =>
        api.RecordEventAsync(attemptId, request, ct);

    public Task SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct) =>
        api.SubmitAsync(attemptId, request, ct);
}
