using System.Net;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class RoleTests(ApiFixture api)
{
    private readonly World _world = new(api);

    private async Task<HttpClient> SuperAdmin() => await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));

    [Fact]
    public async Task Staff_can_read_roles_and_capabilities_but_students_cannot()
    {
        var s = await _world.SectionAsync();
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));

        var roles = await (await faculty.GetAsync("/api/roles")).ReadAsync<List<RoleDto>>();
        Assert.Contains(roles, r => r.Name == RoleNames.Hod && r.Rank == 700);
        var caps = await (await faculty.GetAsync("/api/capabilities")).ReadAsync<List<Capability>>();
        Assert.Equal(Enum.GetValues<Capability>().Length, caps.Count);
        await (await student.GetAsync("/api/roles")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_creates_a_custom_role_and_edits_its_grid()
    {
        var admin = await SuperAdmin();
        var role = await (await admin.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Lab In-charge"), "Runs labs", 350, ScopeLevels.Department)))
            .ReadAsync<RoleDto>(HttpStatusCode.Created);

        var updated = await (await admin.PutJsonAsync($"/api/roles/{role.Id}/capabilities",
                new List<GrantDto> { new(Capability.ViewResults, Reach.InScope), new(Capability.ResetAttempts, Reach.OwnOnly) }))
            .ReadAsync<RoleDto>();

        Assert.Equal(2, updated.Grants.Count);
        var audit = await _world.LastAuditAsync();
        Assert.Equal("role.grants", audit.Action);
        Assert.Contains("ResetAttempts", audit.AfterJson);
    }

    [Fact]
    public async Task You_cannot_grant_what_you_do_not_hold_or_touch_roles_at_your_rank()
    {
        var roleAdminRole = await _world.CustomRoleAsync(World.Unique("Role Admin"), 500, ScopeLevels.Institution, (Capability.ManageRoles, Reach.InScope));
        var roleAdmin = await _world.BareUserAsync();
        await _world.WithDb(db => { db.RoleAssignments.Add(new() { UserId = roleAdmin.Id, RoleId = roleAdminRole, CreatedAt = _world.Now }); return db.SaveChangesAsync(); });
        var client = await _world.LoginAsync(roleAdmin);

        var low = await (await client.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Helper"), null, 100, ScopeLevels.Department))).ReadAsync<RoleDto>(HttpStatusCode.Created);
        await (await client.PutJsonAsync($"/api/roles/{low.Id}/capabilities", new List<GrantDto> { new(Capability.ViewAudit, Reach.InScope) }))
            .ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await client.PutJsonAsync($"/api/roles/{low.Id}/capabilities", new List<GrantDto> { new(Capability.ManageRoles, Reach.InScope) }))
            .ExpectAsync(HttpStatusCode.OK);

        await (await client.PostJsonAsync("/api/roles", new RoleRequest(World.Unique("Too High"), null, 500, ScopeLevels.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        var hodId = await _world.RoleIdAsync(RoleNames.Hod);
        await (await client.PutJsonAsync($"/api/roles/{hodId}", new RoleRequest(RoleNames.Hod, null, 700, ScopeLevels.Department))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_and_student_roles_are_protected()
    {
        var admin = await SuperAdmin();
        var superId = await _world.RoleIdAsync(RoleNames.SuperAdmin);
        var studentId = await _world.RoleIdAsync(RoleNames.Student);

        await (await admin.PutJsonAsync($"/api/roles/{superId}", new RoleRequest("Boss", null, 999, ScopeLevels.Institution))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.DeleteAsync($"/api/roles/{superId}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.PutJsonAsync($"/api/roles/{studentId}/capabilities", new List<GrantDto> { new(Capability.ViewResults, Reach.InScope) })).ValidationFieldsAsync();
        await (await admin.DeleteAsync($"/api/roles/{studentId}")).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Roles_in_use_cannot_be_deleted()
    {
        var admin = await SuperAdmin();
        var s = await _world.SectionAsync();
        var name = World.Unique("Temp Role");
        var roleId = await _world.CustomRoleAsync(name, 200, ScopeLevels.Department);
        var holder = await _world.UserAsync(name, s.Department);

        await (await admin.DeleteAsync($"/api/roles/{roleId}")).ExpectProblemAsync(HttpStatusCode.Conflict);
        await _world.WithDb(db => db.RoleAssignments.Where(a => a.UserId == holder.Id).ExecuteDeleteAsync());
        await (await admin.DeleteAsync($"/api/roles/{roleId}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("role.delete", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task HOD_assigns_lower_roles_inside_the_department_only()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, a.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, a.Department);

        var sub = await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.SubHod), a.BranchId, null, null, null, null))).ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        Assert.Equal(RoleNames.SubHod, sub.RoleName);
        Assert.Equal("assignment.create", (await _world.LastAuditAsync()).Action);

        await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Hod), a.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), b.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.SubHod), a.BranchId, a.ClassId, null, null, null))).ValidationFieldsAsync(); // wrong level
    }

    [Fact]
    public async Task Coordinators_enroll_students_and_students_cannot_hold_staff_roles()
    {
        var s = await _world.SectionAsync();
        var cc = await _world.LoginAsync(await _world.UserAsync(RoleNames.ClassCoordinator, s.Year));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var newcomer = await _world.BareUserAsync(withPin: true);
        var studentRole = await _world.RoleIdAsync(RoleNames.Student);

        await (await cc.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(studentRole, s.BranchId, s.ClassId, s.BatchId, null, null))).ExpectAsync(HttpStatusCode.Created);
        await (await hod.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), s.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nobody_changes_their_own_roles()
    {
        var s = await _world.SectionAsync();
        var hodUser = await _world.UserAsync(RoleNames.Hod, s.Department);
        var hod = await _world.LoginAsync(hodUser);
        var own = await _world.WithDb(db => db.RoleAssignments.SingleAsync(a => a.UserId == hodUser.Id));

        await (await hod.PostJsonAsync($"/api/users/{hodUser.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.Faculty), s.BranchId, null, null, null, null))).ExpectProblemAsync(HttpStatusCode.Forbidden);
        await (await hod.DeleteAsync($"/api/assignments/{own.Id}")).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Time_limited_assignments_stop_granting_after_their_end_date()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var target = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var created = await (await hod.PostJsonAsync($"/api/users/{target.Id}/assignments",
            new AssignmentRequest(await _world.RoleIdAsync(RoleNames.SubHod), s.BranchId, null, null, null, _world.Today.AddDays(1))))
            .ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        Assert.True(created.IsActive);

        api.Time.Advance(TimeSpan.FromDays(2));
        var me = await (await (await _world.LoginAsync(target)).GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        Assert.DoesNotContain(me.Capabilities, c => c.RoleName == RoleNames.SubHod);
    }

    [Fact]
    public async Task Super_admin_assignments_never_end_and_updates_and_removals_are_audited()
    {
        var admin = await SuperAdmin();
        var newcomer = await _world.BareUserAsync();
        var superId = await _world.RoleIdAsync(RoleNames.SuperAdmin);

        await (await admin.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(superId, null, null, null, null, _world.Today.AddDays(30)))).ValidationFieldsAsync();
        var a = await (await admin.PostJsonAsync($"/api/users/{newcomer.Id}/assignments",
            new AssignmentRequest(superId, null, null, null, null, null))).ReadAsync<AssignmentDto>(HttpStatusCode.Created);

        await (await admin.PutJsonAsync($"/api/assignments/{a.Id}", new UpdateAssignmentRequest(null, _world.Today.AddDays(5)))).ValidationFieldsAsync();
        await (await admin.PutJsonAsync($"/api/assignments/{a.Id}", new UpdateAssignmentRequest(_world.Today, null))).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("assignment.update", (await _world.LastAuditAsync()).Action);
        await (await admin.DeleteAsync($"/api/assignments/{a.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("assignment.delete", (await _world.LastAuditAsync()).Action);
    }
}
