using System.Net;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Data.Entities;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;
using static Secucode.Contracts.Capability;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class PermissionTests(ApiFixture api)
{
    private readonly World _world = new(api);

    // Spec §5.4, written out independently of DefaultRoles so a wrong seed is caught.
    private static readonly Dictionary<string, Capability[]> InScopeGrants = new()
    {
        [RoleNames.SuperAdmin] = Enum.GetValues<Capability>(),
        [RoleNames.Principal] = Enum.GetValues<Capability>().Except([ManageSettings, ManageMasters, ManageRoles]).ToArray(),
        [RoleNames.ExamController] = [CreateExams, ManageExams, ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit],
        [RoleNames.Hod] = [ManageUsers, AssignRoles, CreateExams, ManageExams, ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit],
        [RoleNames.SubHod] = [ManageUsers, CreateExams, ManageExams, ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit],
        [RoleNames.ClassCoordinator] = [ManageUsers, CreateExams, ResetAttempts, ReopenAttempts, ExtendTime, ViewResults, ViewEvidence],
        [RoleNames.Faculty] = [CreateExams],
        [RoleNames.Student] = [],
    };

    private static readonly Capability[] FacultyOwnOnly = [ResetAttempts, ReopenAttempts, ExtendTime, EditQuestionsAfterAttempts, ViewResults, ViewEvidence, ViewAudit];

    [Fact]
    public async Task Default_roles_grant_exactly_the_spec_capabilities_inside_their_scope_only()
    {
        var home = await _world.SectionAsync();
        var other = await _world.SectionAsync();

        foreach (var (role, granted) in InScopeGrants)
        {
            var scope = role switch
            {
                RoleNames.SuperAdmin or RoleNames.Principal or RoleNames.ExamController => Scope.Institution,
                RoleNames.Hod or RoleNames.SubHod or RoleNames.Faculty => home.Department,
                RoleNames.ClassCoordinator => home.Year,
                _ => home.Batch,
            };
            var user = await _world.UserAsync(role, scope);

            var failures = await _world.AsCaller(user, async (caller, _) =>
            {
                var wrong = new List<string>();
                foreach (var cap in Enum.GetValues<Capability>())
                {
                    var expectedInside = granted.Contains(cap);
                    var expectedOutside = expectedInside && scope == Scope.Institution;
                    if (await caller.CanAsync(cap, home.Batch) != expectedInside) wrong.Add($"{role}/{cap} inside");
                    if (await caller.CanAsync(cap, other.Batch) != expectedOutside) wrong.Add($"{role}/{cap} outside");
                }
                return wrong;
            });
            Assert.Empty(failures);
        }
    }

    [Fact]
    public async Task OwnOnly_grants_apply_to_own_exams_anywhere_and_never_to_colleagues_exams()
    {
        var s = await _world.SectionAsync();
        var me = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var colleague = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var mine = await _world.ExamAsync(me.Id, s.Batch);
        var theirs = await _world.ExamAsync(colleague.Id, s.Batch);

        await _world.AsCaller(me, async (c, _) =>
        {
            foreach (var cap in FacultyOwnOnly)
            {
                Assert.True(await c.CanOnExamAsync(cap, mine), $"{cap} on own exam");
                Assert.False(await c.CanOnExamAsync(cap, theirs), $"{cap} on colleague's exam");
            }
            Assert.True(await c.CanOnExamAsync(ManageExams, mine));   // creator rule
            Assert.False(await c.CanOnExamAsync(ManageExams, theirs));
            return true;
        });
    }

    [Fact]
    public async Task A_multi_role_user_gets_the_union_each_within_its_own_scope()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, a.Department);
        await _world.AssignAsync(user.Id, RoleNames.ClassCoordinator, b.Year);

        await _world.AsCaller(user, async (c, _) =>
        {
            Assert.True(await c.CanAsync(ManageUsers, b.Batch));
            Assert.False(await c.CanAsync(ManageUsers, a.Batch));
            Assert.True(await c.CanAsync(CreateExams, a.Batch));
            Assert.True(await c.CanAsync(CreateExams, b.Batch));
            return true;
        });
    }

    [Fact]
    public async Task Expired_and_future_assignments_grant_nothing_until_they_are_in_force()
    {
        var s = await _world.SectionAsync();
        var expired = await _world.UserAsync(RoleNames.Hod, s.Department, validTo: _world.Today.AddDays(-1));
        var future = await _world.UserAsync(RoleNames.Hod, s.Department, validFrom: _world.Today.AddDays(1));

        Assert.False(await _world.AsCaller(expired, (c, _) => c.CanAsync(ManageExams, s.Batch)));
        Assert.False(await _world.AsCaller(future, (c, _) => c.CanAsync(ManageExams, s.Batch)));

        api.Time.Advance(TimeSpan.FromDays(2));
        Assert.True(await _world.AsCaller(future, (c, _) => c.CanAsync(ManageExams, s.Batch)));
    }

    [Fact]
    public async Task Deactivated_users_have_no_permissions()
    {
        var s = await _world.SectionAsync();
        var hod = await _world.UserAsync(RoleNames.Hod, s.Department, active: false);
        Assert.False(await _world.AsCaller(hod, (c, _) => c.CanAsync(ManageExams, s.Batch)));
    }

    [Fact]
    public async Task Removing_a_role_takes_effect_on_the_very_next_request()
    {
        var s = await _world.SectionAsync();
        var user = await _world.UserAsync(RoleNames.Faculty, s.Department);
        var hodAssignment = await _world.AssignAsync(user.Id, RoleNames.Hod, s.Department);
        var client = await _world.LoginAsync(user);

        var before = await (await client.GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        Assert.Contains(before.Capabilities, c => c.Capability == AssignRoles);

        await _world.WithDb(db => db.RoleAssignments.Where(a => a.Id == hodAssignment).ExecuteDeleteAsync());

        var after = await (await client.GetAsync("/api/auth/me")).ReadAsync<MeResponse>();
        Assert.DoesNotContain(after.Capabilities, c => c.Capability == AssignRoles);
    }

    [Fact]
    public async Task User_management_respects_rank_scope_and_self()
    {
        var a = await _world.SectionAsync();
        var b = await _world.SectionAsync();
        var hod = await _world.UserAsync(RoleNames.Hod, a.Department);
        var facultyA = await _world.UserAsync(RoleNames.Faculty, a.Department);
        var facultyB = await _world.UserAsync(RoleNames.Faculty, b.Department);
        var principal = await _world.UserAsync(RoleNames.Principal, Scope.Institution);
        var superAdmin = await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution);
        var otherSuperAdmin = await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution);

        Assert.True(await CanManage(hod, facultyA));
        Assert.False(await CanManage(hod, facultyB));   // outside scope
        Assert.False(await CanManage(hod, principal));  // senior
        Assert.False(await CanManage(hod, hod));        // self
        Assert.False(await CanManage(principal, superAdmin));
        Assert.True(await CanManage(superAdmin, otherSuperAdmin)); // only Super Admins manage Super Admins
    }

    private Task<bool> CanManage(TestUser actor, TestUser target) => _world.AsCaller(actor, async (c, db) =>
    {
        var user = await db.Users.Include(u => u.Assignments).ThenInclude(x => x.Role).SingleAsync(u => u.Id == target.Id);
        return await c.CanManageUserAsync(ManageUsers, user);
    });
}
