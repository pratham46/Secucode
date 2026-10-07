using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Secucode.Api.Data;

public static class DatabaseSetup
{
    // Pinned so neither startup nor `dotnet ef` has to connect just to detect the version.
    public static readonly ServerVersion ServerVersion = new MySqlServerVersion(new Version(8, 0, 36));

    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.EnsureReferenceDataAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }
}

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseMySql("Server=localhost;Database=secucode_api", DatabaseSetup.ServerVersion).Options);
}
