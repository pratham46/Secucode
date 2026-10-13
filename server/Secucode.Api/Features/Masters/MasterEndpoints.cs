using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Masters;

public static class MasterEndpoints
{
    public static void MapMasters(this IEndpointRouteBuilder api)
    {
        Map<Branch>(api, "branches", "Department",
            (db, id) => db.RoleAssignments.AnyAsync(a => a.BranchId == id),
            (db, id) => db.Exams.AnyAsync(e => e.BranchId == id));
        Map<SchoolClass>(api, "classes", "Class",
            (db, id) => db.RoleAssignments.AnyAsync(a => a.ClassId == id),
            (db, id) => db.Exams.AnyAsync(e => e.ClassId == id));
        Map<Data.Entities.Batch>(api, "batches", "Batch",
            (db, id) => db.RoleAssignments.AnyAsync(a => a.BatchId == id),
            (db, id) => db.Exams.AnyAsync(e => e.BatchId == id));
    }

    private static void Map<T>(IEndpointRouteBuilder api, string path, string entityType,
        params Func<AppDbContext, int, Task<bool>>[] usages) where T : MasterEntity, new()
    {
        var isDepartment = typeof(T) == typeof(Branch); // department changes are visible to department-scoped auditors
        var g = api.MapGroup("/" + path);

        g.MapGet("", async (AppDbContext db) =>
            await db.Set<T>().OrderBy(x => x.Name).Select(x => new MasterDto(x.Id, x.Name)).ToListAsync());

        g.MapPost("", async (MasterRequest req, AppDbContext db, Caller caller, AuditWriter audit) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageMasters);
            var name = ValidName(req);
            if (await db.Set<T>().AnyAsync(x => x.Name == name))
                throw ApiException.Conflict($"'{name}' already exists.");

            await using var tx = await db.Database.BeginTransactionAsync();
            var item = new T { Name = name };
            db.Set<T>().Add(item);
            await db.SaveChangesAsync();
            audit.Record(caller.UserId, $"{path}.create", entityType, item.Id, after: new { item.Name }, branchId: isDepartment ? item.Id : null);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/{path}/{item.Id}", new MasterDto(item.Id, item.Name));
        });

        g.MapPut("/{id:int}", async (int id, MasterRequest req, AppDbContext db, Caller caller, AuditWriter audit) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageMasters);
            var item = await db.Set<T>().FindAsync(id) ?? throw ApiException.NotFound(entityType);
            var name = ValidName(req);
            if (await db.Set<T>().AnyAsync(x => x.Name == name && x.Id != id))
                throw ApiException.Conflict($"'{name}' already exists.");

            var before = new { item.Name };
            item.Name = name;
            audit.Record(caller.UserId, $"{path}.update", entityType, item.Id, before, new { item.Name }, branchId: isDepartment ? item.Id : null);
            await db.SaveChangesAsync();
            return new MasterDto(item.Id, item.Name);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, Caller caller, AuditWriter audit) =>
        {
            await caller.RequireInstitutionAsync(Capability.ManageMasters);
            var item = await db.Set<T>().FindAsync(id) ?? throw ApiException.NotFound(entityType);
            foreach (var inUse in usages)
                if (await inUse(db, id))
                    throw ApiException.Conflict($"This {entityType.ToLowerInvariant()} is in use and can't be deleted.");

            db.Set<T>().Remove(item);
            audit.Record(caller.UserId, $"{path}.delete", entityType, item.Id, before: new { item.Name }, branchId: isDepartment ? item.Id : null);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static string ValidName(MasterRequest req)
    {
        var name = req.Name?.Trim() ?? "";
        new Validator().Rule(name.Length is >= 1 and <= 100, "name", "Name must be 1 to 100 characters.").ThrowIfInvalid();
        return name;
    }
}
