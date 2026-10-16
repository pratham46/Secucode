using System.Net.Http;
using System.Text.Json;

namespace Secucode.Desktop.Api;

// Every failed API call surfaces as this: the server's problem+json, or Status 0 when the server can't be reached.
public sealed class ApiError(int status, string title, string? detail = null, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
    public bool IsNetwork => Status == 0;

    // What a screen shows in its InfoBar.
    public string Display =>
        IsNetwork ? $"{Title}. Check the server address and that the SecuCode server is running."
        : Errors.Count > 0 ? string.Join("\n", Errors.SelectMany(e => e.Value))
        : Detail ?? Title;

    public static ApiError Network(Exception inner) => new(0, "Can't reach the server", inner.Message);

    public static async Task<ApiError> FromResponseAsync(HttpResponseMessage res)
    {
        var status = (int)res.StatusCode;
        var fallback = res.ReasonPhrase ?? $"HTTP {status}";
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string? Str(string name) =>
                root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            Dictionary<string, string[]>? errors = null;
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                errors = e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Array
                    ? p.Value.EnumerateArray().Select(v => v.GetString() ?? "").ToArray()
                    : [p.Value.ToString()]);

            return new ApiError(status, Str("title") ?? fallback, Str("detail"), errors);
        }
        catch (JsonException)
        {
            return new ApiError(status, fallback);
        }
    }
}
