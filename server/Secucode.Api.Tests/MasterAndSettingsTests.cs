using System.Net;
using System.Text.Json;
using Secucode.Api.Auth;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class MasterAndSettingsTests(ApiFixture api)
{
    private readonly World _world = new(api);

    [Fact]
    public async Task Super_admin_manages_masters_and_every_change_is_audited()
    {
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        var name = World.Unique("Civil");

        var created = await (await admin.PostJsonAsync("/api/branches", new MasterRequest(name))).ReadAsync<MasterDto>(HttpStatusCode.Created);
        Assert.Equal(name, created.Name);
        Assert.Equal("branches.create", (await _world.LastAuditAsync()).Action);

        var renamed = await (await admin.PutJsonAsync($"/api/branches/{created.Id}", new MasterRequest(name + " Eng"))).ReadAsync<MasterDto>();
        Assert.Equal(name + " Eng", renamed.Name);
        var update = await _world.LastAuditAsync();
        Assert.Equal("branches.update", update.Action);
        Assert.Contains(name, update.BeforeJson);

        await (await admin.PostJsonAsync("/api/branches", new MasterRequest(name + " Eng"))).ExpectProblemAsync(HttpStatusCode.Conflict);

        await (await admin.DeleteAsync($"/api/branches/{created.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal("branches.delete", (await _world.LastAuditAsync()).Action);
    }

    [Fact]
    public async Task Masters_in_use_cannot_be_deleted()
    {
        var s = await _world.SectionAsync();
        await _world.UserAsync(RoleNames.Faculty, s.Department);
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));

        await (await admin.DeleteAsync($"/api/branches/{s.BranchId}")).ExpectProblemAsync(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Everyone_can_list_masters_but_only_ManageMasters_can_change_them()
    {
        var s = await _world.SectionAsync();
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));
        var principal = await _world.LoginAsync(await _world.UserAsync(RoleNames.Principal, Scope.Institution));

        var list = await (await student.GetAsync("/api/branches")).ReadAsync<List<MasterDto>>();
        Assert.Contains(list, b => b.Id == s.BranchId);

        await (await principal.PostJsonAsync("/api/classes", new MasterRequest(World.Unique("X")))).ExpectProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Master_names_are_validated()
    {
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        Assert.Equal(["name"], await (await admin.PostJsonAsync("/api/batches", new MasterRequest("  "))).ValidationFieldsAsync());
    }

    [Fact]
    public async Task Staff_read_settings_and_only_ManageSettings_changes_them_with_an_audit_row()
    {
        var s = await _world.SectionAsync();
        var student = await _world.LoginAsync(await _world.UserAsync(RoleNames.Student, s.Batch));
        var faculty = await _world.LoginAsync(await _world.UserAsync(RoleNames.Faculty, s.Department));
        var hod = await _world.LoginAsync(await _world.UserAsync(RoleNames.Hod, s.Department));
        var admin = await _world.LoginAsync(await _world.UserAsync(RoleNames.SuperAdmin, Scope.Institution));
        try
        {
            await (await student.GetAsync("/api/settings")).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.True((await (await faculty.GetAsync("/api/settings")).ReadAsync<SettingsDto>()).AllowAttemptReset);

            var off = new SettingsDto(true, false, true, true);
            await (await hod.PutJsonAsync("/api/settings", off)).ExpectProblemAsync(HttpStatusCode.Forbidden);
            Assert.False((await (await admin.PutJsonAsync("/api/settings", off)).ReadAsync<SettingsDto>()).AllowAttemptReset);

            var audit = await _world.LastAuditAsync();
            Assert.Equal("settings.update", audit.Action);
            // MySQL normalises JSON columns (spacing, key order), so parse rather than string-match.
            Assert.True(JsonDocument.Parse(audit.BeforeJson!).RootElement.GetProperty("allowAttemptReset").GetBoolean());
            Assert.False(JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("allowAttemptReset").GetBoolean());
        }
        finally
        {
            await _world.SetSwitchesAsync(true);
        }
    }
}
