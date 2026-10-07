using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data.Entities;

namespace Secucode.Api.Data;

public static class Seeder
{
    // Reference data the API can't work without. Safe to run on every startup.
    public static async Task EnsureReferenceDataAsync(AppDbContext db, TimeProvider time)
    {
        var now = time.UtcNow();
        if (!await db.Roles.AnyAsync())
            db.Roles.AddRange(DefaultRoles.Create(now));
        if (!await db.SystemSettings.AnyAsync())
            db.SystemSettings.Add(new SystemSettings { Id = 1, UpdatedAt = now });
        await db.SaveChangesAsync();
    }
}
