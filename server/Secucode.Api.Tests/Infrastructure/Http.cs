using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Secucode.Contracts;

namespace Secucode.Api.Tests.Infrastructure;

public static class Http
{
    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient c, string url, T body) => c.PostAsJsonAsync(url, body, Json.Options);
    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient c, string url, T body) => c.PutAsJsonAsync(url, body, Json.Options);
    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient c, string url, T body) => c.PatchAsJsonAsync(url, body, Json.Options);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await r.ExpectAsync(expected);
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    public static async Task<JsonElement> ExpectAsync(this HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {(int)expected} {expected} but got {(int)r.StatusCode} {r.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) || r.Content.Headers.ContentType?.MediaType?.Contains("json") != true ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    // Asserts an RFC 7807 problem response and returns its "detail".
    public static async Task<string> ExpectProblemAsync(this HttpResponseMessage r, HttpStatusCode expected)
    {
        var json = await r.ExpectAsync(expected);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return json.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
    }

    public static async Task<string[]> ValidationFieldsAsync(this HttpResponseMessage r)
    {
        var json = await r.ExpectAsync(HttpStatusCode.BadRequest);
        return json.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray();
    }
}
