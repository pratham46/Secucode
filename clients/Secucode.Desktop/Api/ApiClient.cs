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
        try
        {
            if (RefreshToken is not null)
            {
                // The token is read per send: a 401 may rotate it via refresh before the retry. Logout is already ending the session, so no SessionExpired.
                using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
                {
                    Content = JsonContent.Create(new RefreshRequest(RefreshToken ?? ""), options: Json.Options),
                }, default, signalExpiry: false);
            }
        }
        catch (ApiError) { } // signing out locally matters more than telling the server
        finally { ClearTokens(); }
    }

    private void SetTokens(LoginResponse r)
    {
        lock (_gate) { _accessToken = r.AccessToken; _refreshToken = r.RefreshToken; _expiresAt = r.ExpiresAt; }
    }

    // Returns true only for the call that actually signed the user out.
    private bool ClearTokens()
    {
        bool was;
        lock (_gate) { was = _accessToken is not null; _accessToken = null; _refreshToken = null; }
        Me = null;
        return was;
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

    private Task<bool>? _refreshing;

    // Raised (on a background thread) when the server refused to refresh: the user must sign in again.
    public event EventHandler? SessionExpired;

    // `build` makes a fresh request each time: a request message can be sent only once, and a retry needs another.
    internal async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct, bool authenticated = true, bool signalExpiry = true)
    {
        var usedToken = AccessToken;
        var res = await SendOnceAsync(build, authenticated, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && authenticated && usedToken is not null)
        {
            res.Dispose();
            if (!await RefreshAsync(usedToken))
            {
                if (ClearTokens() && signalExpiry) SessionExpired?.Invoke(this, EventArgs.Empty);
                throw new ApiError(401, "Session expired", "Your session expired. Please sign in again.");
            }
            res = await SendOnceAsync(build, authenticated, ct);
        }
        if (!res.IsSuccessStatusCode)
        {
            using (res) throw await ApiError.FromResponseAsync(res);
        }
        return res;
    }

    // For SignalR: a token that is valid for at least another minute. Offline, the old token is returned and the hub retries.
    public async Task<string?> FreshAccessTokenAsync()
    {
        string? token;
        DateTime expires;
        lock (_gate) { token = _accessToken; expires = _expiresAt; }
        if (token is null) return null;
        if (expires - DateTime.UtcNow > TimeSpan.FromMinutes(1)) return token;
        try { await RefreshAsync(token); }
        catch (ApiError) { }
        return AccessToken;
    }

    // Concurrent 401s share one refresh. A caller whose token was already replaced just retries with the new one.
    private Task<bool> RefreshAsync(string staleToken)
    {
        lock (_gate)
        {
            if (_accessToken != staleToken) return Task.FromResult(_accessToken is not null);
            if (_refreshToken is null) return Task.FromResult(false);
            return _refreshing ??= DoRefreshAsync(_refreshToken);
        }
    }

    private async Task<bool> DoRefreshAsync(string refreshToken)
    {
        await Task.Yield(); // makes sure _refreshing is assigned before the finally below clears it
        try
        {
            using var res = await SendOnceAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new RefreshRequest(refreshToken), options: Json.Options),
            }, authenticated: false, default);
            if (res.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return false;
            if (!res.IsSuccessStatusCode) throw await ApiError.FromResponseAsync(res); // 5xx, 429...: not a refusal, keep the session
            var login = (await res.Content.ReadFromJsonAsync<LoginResponse>(Json.Options))!;
            lock (_gate)
            {
                if (_refreshToken != refreshToken) return false; // signed out (or replaced) while refreshing
                SetTokens(login);
            }
            return true;
        }
        finally
        {
            lock (_gate) _refreshing = null;
        }
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
