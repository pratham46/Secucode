using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private HttpClient Anonymous => api.Factory.CreateClient();

    [Fact]
    public async Task Login_returns_tokens_and_the_users_assignments()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.UserAsync(RoleNames.Hod, s.Department);

        var login = await _world.LoginResponseAsync(hod);

        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.False(string.IsNullOrEmpty(login.RefreshToken));
        Assert.Equal(hod.Username, login.User.Username);
        var a = Assert.Single(login.User.Assignments);
        Assert.Equal(RoleNames.Hod, a.RoleName);
        Assert.Equal(s.BranchId, a.BranchId);
        Assert.True(a.IsActive);
        Assert.Equal("auth.login", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Wrong_password_is_a_401_problem_and_is_audited()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);

        var res = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, "wrong", ClientKind.Web));

        Assert.Equal("Invalid username or password.", await res.ExpectProblemAsync(HttpStatusCode.Unauthorized));
        var last = await _world.LastAuditAsync();
        Assert.Equal("auth.login_failed", last.Action);
        Assert.Equal(u.Username, last.EntityId);
        Assert.Null(last.ActorUserId);
    }

    [Fact]
    public async Task Inactive_users_cannot_log_in()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department, active: false);
        var res = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, World.Password, ClientKind.Web));
        await res.ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_for_five_minutes()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        for (var i = 0; i < 5; i++)
            await (await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, "wrong", ClientKind.Web)))
                .ExpectProblemAsync(HttpStatusCode.Unauthorized);

        var locked = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, World.Password, ClientKind.Web));
        await locked.ExpectProblemAsync(HttpStatusCode.TooManyRequests);

        api.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await _world.LoginResponseAsync(u);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_revokes_every_session()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var first = await _world.LoginResponseAsync(u);

        var second = await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken))).ReadAsync<LoginResponse>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(second.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_tokens_expire_after_seven_days()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var login = await _world.LoginResponseAsync(u);
        api.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var login = await _world.LoginResponseAsync(u);

        await (await _world.Client(login.AccessToken).PostJsonAsync("/api/auth/logout", new RefreshRequest(login.RefreshToken)))
            .ExpectAsync(HttpStatusCode.NoContent);

        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        await (await Anonymous.GetAsync("/api/auth/me")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Access_tokens_expire_after_fifteen_minutes()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var client = await _world.LoginAsync(u);
        await (await client.GetAsync("/api/auth/me")).ExpectAsync(HttpStatusCode.OK);

        api.Time.Advance(TimeSpan.FromMinutes(16));
        await (await client.GetAsync("/api/auth/me")).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_union_of_active_roles_only()
    {
        var s = await _world.SectionAsync();
        var u = await _world.UserAsync(RoleNames.Faculty, s.Department);
        await _world.AssignAsync(u.Id, RoleNames.Hod, s.Department);
        await _world.AssignAsync(u.Id, RoleNames.ClassCoordinator, s.Year, validTo: _world.Today.AddDays(-1));

        var me = await (await (await _world.LoginAsync(u)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();

        Assert.Equal(3, me.User.Assignments.Count);
        Assert.Contains(me.Capabilities, c => c.Capability == Capability.AssignRoles && c.RoleName == RoleNames.Hod);
        Assert.Contains(me.Capabilities, c => c is { Capability: Capability.ResetAttempts, Reach: Reach.OwnOnly, RoleName: RoleNames.Faculty });
        Assert.DoesNotContain(me.Capabilities, c => c.RoleName == RoleNames.ClassCoordinator);
    }

    [Fact]
    public async Task Parallel_wrong_guesses_still_trigger_the_lockout()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, "wrong", ClientKind.Web))));

        var res = await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(u.Username, World.Password, ClientKind.Web));
        await res.ExpectProblemAsync(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Parallel_refreshes_of_one_token_let_exactly_one_win()
    {
        var u = await _world.UserAsync(RoleNames.Faculty, (await _world.SectionAsync()).Department);
        var login = await _world.LoginResponseAsync(u);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Me_marks_student_assignments()
    {
        var s = await _world.SectionAsync();
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);
        var faculty = await _world.UserAsync(RoleNames.Faculty, s.Department);

        var studentMe = await (await (await _world.LoginAsync(student)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        var facultyMe = await (await (await _world.LoginAsync(faculty)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();

        Assert.True(Assert.Single(studentMe.User.Assignments).IsStudent);
        Assert.False(Assert.Single(facultyMe.User.Assignments).IsStudent);
    }
}
