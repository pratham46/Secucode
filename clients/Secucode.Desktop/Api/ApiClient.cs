using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Secucode.Contracts;

namespace Secucode.Desktop.Api;

// The app's only door to the server. Tokens live in memory; every failure becomes an ApiError.
public sealed partial class ApiClient(HttpClient http)
{
    private readonly object _gate = new();
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _expiresAt;

    public HttpClient Http => http;
    public MeResponse? Me { get; private set; }
    public bool IsSignedIn { get { lock (_gate) return _accessToken is not null; } }

    internal string? AccessToken { get { lock (_gate) return _accessToken; } set { lock (_gate) _accessToken = value; } }
    internal string? RefreshToken { get { lock (_gate) return _refreshToken; } set { lock (_gate) _refreshToken = value; } }
    internal DateTime ExpiresAt { get { lock (_gate) return _expiresAt; } set { lock (_gate) _expiresAt = value; } }

    public async Task<MeResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var login = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login",
            new LoginRequest(username, password, ClientKind.Desktop), ct, authenticated: false);
        SetTokens(login);
        Me = await SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, ct);
        return Me;
    }

    public async Task LogoutAsync()
    {
        var refresh = RefreshToken;
        try
        {
            if (refresh is not null)
                using (await SendAsync(HttpMethod.Post, "api/auth/logout", new RefreshRequest(refresh), default)) { }
        }
        catch (ApiError) { } // signing out locally matters more than telling the server
        finally { ClearTokens(); }
    }

    private void SetTokens(LoginResponse r)
    {
        lock (_gate) { _accessToken = r.AccessToken; _refreshToken = r.RefreshToken; _expiresAt = r.ExpiresAt; }
    }

    private void ClearTokens()
    {
        lock (_gate) { _accessToken = null; _refreshToken = null; }
        Me = null;
    }

    // ---- transport ----

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, bool authenticated = true)
    {
        using var res = await SendAsync(method, path, body, ct, authenticated);
        return (await res.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    internal async Task<T> SendAsync<T>(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        using var res = await SendAsync(build, ct);
        return (await res.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    internal Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool authenticated = true) =>
        SendAsync(() =>
        {
            var req = new HttpRequestMessage(method, path);
            if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json.Options);
            return req;
        }, ct, authenticated);

    // `build` makes a fresh request each time: a request message can be sent only once, and a retry needs another.
    internal async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct, bool authenticated = true)
    {
        var res = await SendOnceAsync(build, authenticated, ct);
        if (!res.IsSuccessStatusCode)
        {
            using (res) throw await ApiError.FromResponseAsync(res);
        }
        return res;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> build, bool authenticated, CancellationToken ct)
    {
        using var req = build();
        if (authenticated && AccessToken is { } token)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            return await http.SendAsync(req, ct);
        }
        catch (HttpRequestException e) { throw ApiError.Network(e); }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw ApiError.Network(e); } // HttpClient timeout
    }
}
