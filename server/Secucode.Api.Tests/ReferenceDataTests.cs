using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Api.Tests.Infrastructure;
using Secucode.Contracts;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReferenceDataTests(ApiFixture api)
{
    [Fact]
    public async Task Default_roles_and_settings_are_seeded_at_startup()
    {
        using var scope = api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await db.Roles.Include(r => r.Capabilities).AsNoTracking().ToDictionaryAsync(r => r.Name);

        Assert.Equal(
            new Dictionary<string, int>
            {
                [RoleNames.SuperAdmin] = 1000, [RoleNames.Principal] = 900, [RoleNames.ExamController] = 800,
                [RoleNames.Hod] = 700, [RoleNames.SubHod] = 600, [RoleNames.ClassCoordinator] = 400,
                [RoleNames.Faculty] = 300, [RoleNames.Student] = 0,
            },
            roles.Where(r => r.Key is RoleNames.SuperAdmin or RoleNames.Principal or RoleNames.ExamController or RoleNames.Hod
                or RoleNames.SubHod or RoleNames.ClassCoordinator or RoleNames.Faculty or RoleNames.Student)
                .ToDictionary(r => r.Key, r => r.Value.Rank));

        var superAdmin = roles[RoleNames.SuperAdmin];
        Assert.True(superAdmin.IsSystem);
        Assert.Equal(Enum.GetValues<Capability>().Length, superAdmin.Capabilities.Count);
        Assert.All(superAdmin.Capabilities, c => Assert.Equal(Reach.InScope, c.Reach));

        var faculty = roles[RoleNames.Faculty];
        Assert.Equal(Reach.InScope, faculty.Capabilities.Single(c => c.Capability == Capability.CreateExams).Reach);
        Assert.Equal(Reach.OwnOnly, faculty.Capabilities.Single(c => c.Capability == Capability.ResetAttempts).Reach);
        Assert.Equal(ScopeLevels.Department | ScopeLevels.Year | ScopeLevels.Batch, faculty.AllowedScopeLevels);

        var student = roles[RoleNames.Student];
        Assert.True(student.IsStudent);
        Assert.Empty(student.Capabilities);
        Assert.Equal(ScopeLevels.Batch, student.AllowedScopeLevels);

        var settings = await db.SystemSettings.SingleAsync();
        Assert.True(settings.AllowQuestionEditAfterAttempts && settings.AllowAttemptReset && settings.AllowAttemptReopen && settings.AllowTimeExtension);
    }
}
