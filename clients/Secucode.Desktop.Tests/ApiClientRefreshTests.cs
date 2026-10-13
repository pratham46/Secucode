using System.Net.Http;
using System.Net.Http.Json;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ApiClientRefreshTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private (ApiClient Client, CountingHandler Counter) NewClient()
    {
        var counter = new CountingHandler(api.Factory.Server.CreateHandler());
        return (new ApiClient(new HttpClient(counter) { BaseAddress = api.Factory.Server.BaseAddress }), counter);
    }

    private async Task<(ApiClient, CountingHandler, TestUser)> SignedInAsync()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var (client, counter) = NewClient();
        await client.LoginAsync(user.Username, World.Password);
        return (client, counter, user);
    }

    [Fact]
    public async Task Concurrent_401s_share_one_refresh_and_every_call_is_retried()
    {
        var (client, counter, user) = await SignedInAsync();
        client.AccessToken = "not-a-valid-token";

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default)));

        Assert.All(results, r => Assert.Equal(user.Id, r.User.Id));
        Assert.Equal(1, counter.Count("/api/auth/refresh"));
        Assert.True(client.IsSignedIn);
    }

    [Fact]
    public async Task A_revoked_session_raises_SessionExpired_and_signs_out()
    {
        var (client, _, _) = await SignedInAsync();
        var expired = 0;
        client.SessionExpired += (_, _) => expired++;
        client.AccessToken = "not-a-valid-token";
        client.RefreshToken = "revoked-or-unknown";

        var e = await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default));

        Assert.Equal(401, e.Status);
        Assert.Equal(1, expired);
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task FreshAccessTokenAsync_refreshes_only_near_expiry()
    {
        var (client, counter, _) = await SignedInAsync();

        Assert.Equal(client.AccessToken, await client.FreshAccessTokenAsync());
        Assert.Equal(0, counter.Count("/api/auth/refresh"));

        client.ExpiresAt = DateTime.UtcNow.AddSeconds(30);
        var token = await client.FreshAccessTokenAsync();

        Assert.Equal(1, counter.Count("/api/auth/refresh"));
        Assert.Equal(client.AccessToken, token);
        Assert.True(client.ExpiresAt > DateTime.UtcNow.AddMinutes(5));
    }

    [Fact]
    public async Task Calls_without_a_session_are_not_refreshed()
    {
        var (client, counter) = NewClient();

        var e = await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default));

        Assert.Equal(401, e.Status);
        Assert.Equal(0, counter.Count("/api/auth/refresh"));
    }

    private sealed class FuncHandler(HttpMessageHandler inner, Func<HttpRequestMessage, Task<HttpResponseMessage?>> intercept) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            await intercept(request) ?? await base.SendAsync(request, ct);
    }

    [Fact]
    public async Task Concurrent_calls_with_a_revoked_session_raise_SessionExpired_once()
    {
        var (client, _, _) = await SignedInAsync();
        var expired = 0;
        client.SessionExpired += (_, _) => Interlocked.Increment(ref expired);
        client.AccessToken = "not-a-valid-token";
        client.RefreshToken = "revoked-or-unknown";

        var calls = Enumerable.Range(0, 5).Select(async _ =>
            await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default)));
        var errors = await Task.WhenAll(calls);

        Assert.All(errors, e => Assert.Equal(401, e.Status));
        Assert.Equal(1, expired);
    }

    [Fact]
    public async Task A_server_error_during_refresh_keeps_the_session()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var down = new FuncHandler(api.Factory.Server.CreateHandler(), r =>
            Task.FromResult<HttpResponseMessage?>(r.RequestUri!.AbsolutePath == "/api/auth/refresh"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) : null));
        var client = new ApiClient(new HttpClient(down) { BaseAddress = api.Factory.Server.BaseAddress });
        await client.LoginAsync(user.Username, World.Password);
        var expired = 0;
        client.SessionExpired += (_, _) => expired++;
        client.AccessToken = "not-a-valid-token";

        var e = await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<MeResponse>(HttpMethod.Get, "api/auth/me", null, default));

        Assert.Equal(503, e.Status);
        Assert.True(client.IsSignedIn);
        Assert.Equal(0, expired);
    }

    [Fact]
    public async Task Logout_with_an_expired_access_token_revokes_the_rotated_refresh_token()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        string? sent = null;
        var spy = new FuncHandler(api.Factory.Server.CreateHandler(), async r =>
        {
            if (r.RequestUri!.AbsolutePath == "/api/auth/logout")
                sent = await r.Content!.ReadAsStringAsync();
            return null;
        });
        var client = new ApiClient(new HttpClient(spy) { BaseAddress = api.Factory.Server.BaseAddress });
        await client.LoginAsync(user.Username, World.Password);
        var original = client.RefreshToken!;
        var expired = 0;
        client.SessionExpired += (_, _) => expired++;
        client.AccessToken = "not-a-valid-token";

        await client.LogoutAsync();
        Assert.False(client.IsSignedIn);
        Assert.Equal(0, expired);
        Assert.NotNull(sent);
        Assert.DoesNotContain(original, sent); // the last logout send carries the rotated token
    }
}
