using System.Net;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class UserTests(ApiFixture api)
{
    private readonly World _world = new(api);
    private HttpClient Anonymous => api.Factory.CreateClient();

    private async Task<CreateUserRequest> NewUser(string role, Scope scope, string? pin = null) =>
        new(World.Unique("new"), "Secret1", "New Person", "new@college.edu", pin,
            [new AssignmentRequest(await _world.RoleIdAsync(role), scope.BranchId, scope.ClassId, scope.BatchId, null, null)]);

    [Fact]
    public async Task HOD_creates_faculty_in_own_department_who_can_then_log_in()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var req = await NewUser(RoleNames.Faculty, s.Department);

        var created = await (await hod.PostJsonAsync("/api/users", req)).ReadAsync<UserDto>(HttpStatusCode.Created);

        Assert.Equal(RoleNames.Faculty, Assert.Single(created.Assignments).RoleName);
        var audit = await _world.LastAuditAsync();
        Assert.Equal("user.create", audit.Action);
        Assert.Equal(s.BranchId, audit.BranchId);
        Assert.DoesNotContain("Secret1", audit.AfterJson);
        await (await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(req.Username, "Secret1", ClientKind.Web))).ExpectAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HOD_cannot_create_staff_in_another_department()
    {
        var mine = await _world.SectionAsync();
        var other = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, mine.Department));
        await (await hod.PostJsonAsync("/api/users", await NewUser(RoleNames.Faculty, other.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Class_coordinator_enrolls_students_but_cannot_create_staff()
    {
        var s = await _world.SectionAsync();
        var cc = await _world.LoginAsync(await _world.UserAsync(RoleNames.ClassCoordinator, s.Year));

        await (await cc.PostJsonAsync("/api/users", await NewUser(RoleNames.Student, s.Batch, pin: "4321"))).ExpectAsync(HttpStatusCode.Created);
        await (await cc.PostJsonAsync("/api/users", await NewUser(RoleNames.Faculty, s.Year))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Students_need_a_pin_and_cannot_also_hold_staff_roles()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));

        var noPin = await (await hod.PostJsonAsync("/api/users", await NewUser(RoleNames.Student, s.Batch))).ValidationFieldsAsync();
        Assert.Contains("pin", noPin);

        var both = await NewUser(RoleNames.Student, s.Batch, pin: "1111");
        both = both with { Assignments = [.. both.Assignments, new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), s.BranchId, null, null, null, null)] };
        await (await hod.PostJsonAsync("/api/users", both)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_input_reports_every_bad_field_and_duplicate_usernames_conflict()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var bad = new CreateUserRequest("a b", "123", "", "not-an-email", "12", []);

        var fields = await (await hod.PostJsonAsync("/api/users", bad)).ValidationFieldsAsync();
        Assert.Equal(["assignments", "email", "fullName", "password", "pin", "username"], fields.OrderBy(f => f).ToArray());

        var existing = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var dup = (await NewUser(RoleNames.Faculty, s.Department)) with { Username = existing.Username };
        await (await hod.PostJsonAsync("/api/users", dup)).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_user_list_only_shows_people_the_caller_can_manage()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var mine = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var notMine = await _world.UserAsync(RoleNames.Faculty, b.Department);
        var senior = await _world.UserAsync(RoleNames.Principal, Scope.Institution);

        var page = await (await hod.GetAsync($"/api/users?branchId={a.BranchId}")).ReadAsync<PageDto<UserDto>>();
        Assert.Contains(page.Items, u => u.Id == mine.Id);

        var all = await (await hod.GetAsync("/api/users?search=u")).ReadAsync<PageDto<UserDto>>();
        Assert.DoesNotContain(all.Items, u => u.Id == notMine.Id || u.Id == senior.Id);

        var faculty = await _world.LoginAsync(mine);
        await (await faculty.GetAsync("/api/users")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deactivating_a_user_blocks_login_and_kills_existing_sessions()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var session = await _world.LoginResponseAsync(target);

        await (await hod.DeleteAsync($"/api/users/{target.Id}")).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal("user.deactivate", (await _world.LastAuditAsync()).Action);
        await (await Anonymous.PostJsonAsync("/api/auth/login", new LoginRequest(target.Username, World.Password, ClientKind.Web))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_reset_revokes_sessions_and_the_new_password_works()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var session = await _world.LoginResponseAsync(target);

        await (await hod.PostJsonAsync($"/api/users/{target.Id}/reset-password", new ResetPasswordRequest("NewPass9"))).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal("user.reset_password", (await _world.LastAuditAsync()).Action);
        await (await Anonymous.PostJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken))).ExpectProblemAsync(HttpStatusCode.Unauthorized);
        await _world.LoginResponseAsync(target, password: "NewPass9");
    }

    [Fact]
    public async Task Pin_reset_validates_digits()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var student = await _world.UserAsync(RoleNames.Student, s.Batch);

        Assert.Equal(["newPin"], await (await hod.PostJsonAsync($"/api/users/{student.Id}/reset-pin", new ResetPinRequest("12ab"))).ValidationFieldsAsync());
        await (await hod.PostJsonAsync($"/api/users/{student.Id}/reset-pin", new ResetPinRequest("987654"))).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("user.reset_pin", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Nobody_manages_themselves_or_anyone_senior()
    {
        var s = await _world.SectionAsync();
        var hodUser = await _world.UserAsync(RoleNames.Hod, s.Department);
        var hod = await _world.LoginAsync(hodUser);
        var principal = await _world.UserAsync(RoleNames.Principal, Scope.Institution);

        await (await hod.PutJsonAsync($"/api/users/{hodUser.Id}", new UpdateUserRequest("Me", null, true))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.DeleteAsync($"/api/users/{principal.Id}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_super_admins_can_deactivate_super_admins()
    {
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        var principal = await _world.LoginAsync(await _world.UserAsync(RoleNames.Principal, Scope.Institution));
        var target = await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution);

        await (await principal.DeleteAsync($"/api/users/{target.Id}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.DeleteAsync($"/api/users/{target.Id}")).ExpectAsync(HttpStatusCode.NoContent);
    }
}
