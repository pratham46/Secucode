using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Roles;

public static class RoleEndpoints
{
    private const ScopeLevels AllLevels = ScopeLevels.Institution | ScopeLevels.Department | ScopeLevels.Year | ScopeLevels.Batch;

    public static void MapRoles(this IEndpointRouteBuilder api)
    {
        api.MapGet("/capabilities", async (Caller caller) =>
        {
            await caller.RequireStaffAsync();
            return Enum.GetValues<Capability>();
        });

        var g = api.MapGroup("/roles");
        g.MapGet("", async (AppDbContext db, Caller caller) =>
        {
            await caller.RequireStaffAsync();
            var roles = await db.Roles.Include(r => r.Capabilities).AsNoTracking().OrderByDescending(r => r.Rank).ThenBy(r => r.Name).ToListAsync();
            return roles.Select(ToDto).ToList();
        });
        g.MapPost("", CreateAsync);
        g.MapPut("/{id:int}", UpdateAsync);
        g.MapDelete("/{id:int}", DeleteAsync);
        g.MapPut("/{id:int}/capabilities", SetGrantsAsync);
    }

    public static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.Description, r.Rank, r.IsSystem, r.IsStudent, r.AllowedScopeLevels,
        r.Capabilities.OrderBy(c => c.Capability).Select(c => new GrantDto(c.Capability, c.Reach)).ToList());

    private static async Task<IResult> CreateAsync(RoleRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        await caller.RequireInstitutionAsync(Capability.ManageRoles);
        var name = Validate(req, isStudent: false);
        await EnsureBelowCallerAsync(caller, req.Rank);
        if (await db.Roles.AnyAsync(r => r.Name == name))
            throw ApiException.Conflict($"A role named '{name}' already exists.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var role = new Role { Name = name, Description = req.Description?.Trim(), Rank = req.Rank, AllowedScopeLevels = req.AllowedScopeLevels, CreatedAt = time.UtcNow() };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "role.create", "Role", role.Id, after: ToDto(role));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/roles/{role.Id}", ToDto(role));
    }

    private static async Task<RoleDto> UpdateAsync(int id, RoleRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var role = await EditableAsync(db, caller, id);
        var name = Validate(req, role.IsStudent);
        await EnsureBelowCallerAsync(caller, req.Rank);
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id))
            throw ApiException.Conflict($"A role named '{name}' already exists.");

        var before = ToDto(role);
        role.Name = name;
        role.Description = req.Description?.Trim();
        role.Rank = req.Rank;
        role.AllowedScopeLevels = req.AllowedScopeLevels;
        audit.Record(caller.UserId, "role.update", "Role", role.Id, before, ToDto(role));
        await db.SaveChangesAsync();
        return ToDto(role);
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var role = await EditableAsync(db, caller, id);
        if (role.IsStudent)
            throw ApiException.Conflict("The Student role can't be deleted.");
        if (await db.RoleAssignments.AnyAsync(a => a.RoleId == id))
            throw ApiException.Conflict("This role is still assigned to people. Remove those assignments first.");

        var before = ToDto(role);
        db.Roles.Remove(role);
        audit.Record(caller.UserId, "role.delete", "Role", id, before);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<RoleDto> SetGrantsAsync(int id, List<GrantDto> grants, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var role = await EditableAsync(db, caller, id);
        new Validator()
            .Rule(!role.IsStudent || grants.Count == 0, "grants", "Students can't be given capabilities.")
            .Rule(grants.Select(g => g.Capability).Distinct().Count() == grants.Count, "grants", "Each capability can appear only once.")
            .Rule(grants.All(g => Enum.IsDefined(g.Capability) && Enum.IsDefined(g.Reach)), "grants", "Unknown capability or reach.")
            .ThrowIfInvalid();

        // Adding a capability, or widening OwnOnly to InScope, requires holding it for the whole institution.
        foreach (var g in grants)
        {
            var old = role.Capabilities.FirstOrDefault(c => c.Capability == g.Capability);
            var widening = old is null || (old.Reach == Reach.OwnOnly && g.Reach == Reach.InScope);
            if (widening && !await caller.CanAsync(g.Capability, Scope.Institution))
                throw ApiException.Forbidden($"You can't grant {g.Capability} because you don't hold it for the whole institution.");
        }

        var before = ToDto(role).Grants;
        role.Capabilities.RemoveAll(c => grants.All(g => g.Capability != c.Capability));
        foreach (var g in grants)
        {
            var existing = role.Capabilities.FirstOrDefault(c => c.Capability == g.Capability);
            if (existing is null)
                role.Capabilities.Add(new RoleCapability { RoleId = role.Id, Capability = g.Capability, Reach = g.Reach });
            else
                existing.Reach = g.Reach;
        }
        audit.Record(caller.UserId, "role.grants", "Role", role.Id, before, ToDto(role).Grants);
        await db.SaveChangesAsync();
        return ToDto(role);
    }

    private static async Task<Role> EditableAsync(AppDbContext db, Caller caller, int id)
    {
        await caller.RequireInstitutionAsync(Capability.ManageRoles);
        var role = await db.Roles.Include(r => r.Capabilities).SingleOrDefaultAsync(r => r.Id == id) ?? throw ApiException.NotFound("Role");
        if (role.IsSystem)
            throw ApiException.Forbidden("The Super Admin role can't be changed.");
        await EnsureBelowCallerAsync(caller, role.Rank);
        return role;
    }

    private static async Task EnsureBelowCallerAsync(Caller caller, int rank)
    {
        if (rank >= await caller.HighestRankAsync())
            throw ApiException.Forbidden("You can only manage roles ranked below your own.");
    }

    private static string Validate(RoleRequest req, bool isStudent)
    {
        var name = req.Name?.Trim() ?? "";
        var v = new Validator()
            .Rule(name.Length is >= 2 and <= 100, "name", "Name must be 2 to 100 characters.")
            .Rule((req.Description?.Length ?? 0) <= 500, "description", "Description can be up to 500 characters.");
        if (isStudent)
            v.Rule(req.Rank == 0, "rank", "The Student role's rank is fixed at 0.")
             .Rule(req.AllowedScopeLevels == ScopeLevels.Batch, "allowedScopeLevels", "Students are always assigned to a batch.");
        else
            v.Rule(req.Rank is >= 1 and <= 999, "rank", "Rank must be between 1 and 999.")
             .Rule(req.AllowedScopeLevels != ScopeLevels.None && (req.AllowedScopeLevels & ~AllLevels) == 0, "allowedScopeLevels", "Pick at least one valid scope level.");
        v.ThrowIfInvalid();
        return name;
    }
}
