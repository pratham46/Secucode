using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Data;
using Secucode.Api.Tests.Infrastructure;

namespace Secucode.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SeedTests(ApiFixture api)
{
    [Fact]
    public async Task Seed_builds_a_demo_college_once_on_an_empty_database()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(api.ConnectionString("seed_" + Guid.NewGuid().ToString("N")[..8]), DatabaseSetup.ServerVersion).Options;
        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            await Seeder.EnsureReferenceDataAsync(db, api.Time);

            Assert.True(await Seeder.SeedDemoAsync(db, api.Time));

            Assert.Equal(8, await db.Roles.CountAsync());
            Assert.Equal(5, await db.Users.CountAsync());
            var hod = await db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).SingleAsync(u => u.Username == "hod.cse");
            Assert.Equal([RoleNames.Faculty, RoleNames.Hod], hod.Assignments.Select(a => a.Role.Name).OrderBy(n => n));
            Assert.True(BCrypt.Net.BCrypt.Verify("Hod@123", hod.PasswordHash));
            Assert.True(await db.Exams.AnyAsync(e => e.Title == "Sample Coding Test" && e.Questions.Count == 2));

            Assert.False(await Seeder.SeedDemoAsync(db, api.Time)); // idempotent
            Assert.Equal(5, await db.Users.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
