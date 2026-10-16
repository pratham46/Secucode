using System.Net.Http;
using System.Net.Http.Headers;
using Secucode.Contracts;

namespace Secucode.Desktop.Api;

public sealed partial class ApiClient
{
    // ---- exams ----
    public Task<List<StudentExamDto>> StudentExamsAsync(CancellationToken ct = default) =>
        SendAsync<List<StudentExamDto>>(HttpMethod.Get, "api/exams", null, ct);

    public Task<List<ExamDto>> StaffExamsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (fromUtc is { } from) query.Add("from=" + Uri.EscapeDataString(from.ToUniversalTime().ToString("O")));
        if (toUtc is { } to) query.Add("to=" + Uri.EscapeDataString(to.ToUniversalTime().ToString("O")));
        return SendAsync<List<ExamDto>>(HttpMethod.Get, "api/exams" + (query.Count > 0 ? "?" + string.Join("&", query) : ""), null, ct);
    }

    public Task<ExamDto> ExamAsync(int id, CancellationToken ct = default) =>
        SendAsync<ExamDto>(HttpMethod.Get, $"api/exams/{id}", null, ct);

    public Task<ExamDto> CreateExamAsync(ExamRequest request, CancellationToken ct = default) =>
        SendAsync<ExamDto>(HttpMethod.Post, "api/exams", request, ct);

    public Task<ExamDto> UpdateExamAsync(int id, ExamRequest request, CancellationToken ct = default) =>
        SendAsync<ExamDto>(HttpMethod.Put, $"api/exams/{id}", request, ct);

    public async Task SetExamActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Patch, $"api/exams/{id}/active", new SetActiveRequest(isActive), ct);
    }

    public async Task DeleteExamAsync(int id, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"api/exams/{id}", null, ct);
    }

    public Task<List<AttemptSummaryDto>> ExamAttemptsAsync(int examId, CancellationToken ct = default) =>
        SendAsync<List<AttemptSummaryDto>>(HttpMethod.Get, $"api/exams/{examId}/attempts", null, ct);

    // ---- attempts ----
    public Task<AttemptDto> StartAttemptAsync(int examId, string pin, CancellationToken ct = default) =>
        SendAsync<AttemptDto>(HttpMethod.Post, $"api/exams/{examId}/attempts", new StartAttemptRequest(pin), ct);

    public Task<AttemptDetailDto> AttemptAsync(int id, CancellationToken ct = default) =>
        SendAsync<AttemptDetailDto>(HttpMethod.Get, $"api/attempts/{id}", null, ct);

    public async Task SaveCodeAsync(int attemptId, SaveCodeRequest request, CancellationToken ct = default)
    {
        using var _ = await SendAsync(HttpMethod.Put, $"api/attempts/{attemptId}/code", request, ct);
    }

    public Task<AttemptEventDto> RecordEventAsync(int attemptId, AttemptEventRequest request, CancellationToken ct = default) =>
        SendAsync<AttemptEventDto>(HttpMethod.Post, $"api/attempts/{attemptId}/events", request, ct);

    public Task<EvidenceDto> UploadEvidenceAsync(int attemptId, EvidenceKind kind, byte[] bytes, string contentType, CancellationToken ct = default) =>
        SendAsync<EvidenceDto>(() =>
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var extension = contentType switch { "image/png" => ".png", "audio/wav" => ".wav", _ => ".jpg" };
            var form = new MultipartFormDataContent
            {
                { new StringContent(kind.ToString()), "kind" },
                { file, "file", "evidence" + extension },
            };
            return new HttpRequestMessage(HttpMethod.Post, $"api/attempts/{attemptId}/evidence") { Content = form };
        }, ct);

    public Task<AttemptDto> SubmitAsync(int attemptId, SubmitRequest request, CancellationToken ct = default) =>
        SendAsync<AttemptDto>(HttpMethod.Post, $"api/attempts/{attemptId}/submit", request, ct);

    // ---- other ----
    public Task<RunResult> RunAsync(RunRequest request, CancellationToken ct = default) =>
        SendAsync<RunResult>(HttpMethod.Post, "api/run", request, ct);

    public Task<List<MasterDto>> MastersAsync(string kind, CancellationToken ct = default) =>
        SendAsync<List<MasterDto>>(HttpMethod.Get, $"api/{kind}", null, ct);
}
