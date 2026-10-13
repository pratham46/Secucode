using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using Secucode.Desktop.Api;

namespace Secucode.Desktop.Tests;

[Collection(DesktopApiCollection.Name)]
public sealed class ApiClientAuthTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private ApiClient NewClient() => new(api.Factory.CreateClient());

    [Fact]
    public async Task Login_signs_in_as_desktop_and_loads_me()
    {
        var s = await _world.SectionAsync();
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var client = NewClient();

        var me = await client.LoginAsync(student.Username, World.Password);

        Assert.True(client.IsSignedIn);
        Assert.Equal(student.Id, me.User.Id);
        Assert.True(Assert.Single(me.User.Assignments).IsStudent);
        Assert.Same(me, client.Me);
    }

    [Fact]
    public async Task Wrong_password_is_an_ApiError_carrying_the_servers_problem()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = NewClient();

        var e = await Assert.ThrowsAsync<ApiError>(() => client.LoginAsync(user.Username, "wrong"));

        Assert.Equal(401, e.Status);
        Assert.Equal("Invalid username or password.", e.Detail);
        Assert.Equal("Invalid username or password.", e.Display);
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task Unreachable_server_is_a_network_error()
    {
        var client = new ApiClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") });

        var e = await Assert.ThrowsAsync<ApiError>(() => client.LoginAsync("x", "y"));

        Assert.True(e.IsNetwork);
        Assert.Equal(0, e.Status);
        Assert.StartsWith("Can't reach the server", e.Display);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_signs_out()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var client = NewClient();
        await client.LoginAsync(user.Username, World.Password);
        var refresh = client.RefreshToken!;

        await client.LogoutAsync();

        Assert.False(client.IsSignedIn);
        Assert.Null(client.Me);
        var res = await api.Factory.CreateClient().PostJsonAsync("/api/auth/refresh", new RefreshRequest(refresh));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Validation_problems_expose_field_errors()
    {
        var client = NewClient();

        var e = await Assert.ThrowsAsync<ApiError>(() => client.LoginAsync("", ""));

        Assert.Equal(400, e.Status);
        Assert.NotEmpty(e.Errors);
        Assert.Equal(string.Join("\n", e.Errors.SelectMany(x => x.Value)), e.Display);
    }
}
