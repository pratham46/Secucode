using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Users;

// Shared by user creation (Task 6) and the assignment endpoints (Task 7). Spec §5.5.
public static class AssignmentRules
{
    public static async Task<RoleAssignment> BuildAsync(AppDbContext db, Caller caller, AssignmentRequest req,
        IReadOnlyCollection<RoleAssignment> existing, bool userHasPin, bool forNewUser, DateTime now)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == req.RoleId)
            ?? throw ApiException.Validation("roleId", "That role doesn't exist.");
        var scope = new Scope(req.BranchId, req.ClassId, req.BatchId);

        new Validator()
            .Rule(scope.IsValidShape, "scope", "A class needs a department, and a batch needs a class.")
            .Rule(role.AllowedScopeLevels.HasFlag(scope.Level), "scope", $"{role.Name} can't be assigned at {scope.Level} level.")
            .Rule(req.ValidFrom is null || req.ValidTo is null || req.ValidTo >= req.ValidFrom, "validTo", "End date must be on or after the start date.")
            .Rule(!role.IsSystem || req.ValidTo is null, "validTo", "Super Admin assignments can't have an end date.")
            .ThrowIfInvalid();
        await EnsureScopeExistsAsync(db, scope);
        await CheckPermissionAsync(caller, role, scope, forNewUser);

        if (role.IsStudent ? existing.Count > 0 : existing.Any(a => a.Role.IsStudent))
            throw ApiException.Conflict(role.IsStudent ? "A student can't hold any other role." : "A student can't also hold staff roles.");
        if (role.IsStudent && !userHasPin)
            throw ApiException.Validation("pin", "Students need a PIN.");

        return new RoleAssignment
        {
            Role = role, RoleId = role.Id, BranchId = scope.BranchId, ClassId = scope.ClassId, BatchId = scope.BatchId,
            ValidFrom = req.ValidFrom, ValidTo = req.ValidTo, CreatedBy = caller.UserId, CreatedAt = now,
        };
    }

    // Student assignments need ManageUsers; every other role needs AssignRoles; creating a user always needs ManageUsers.
    public static async Task CheckPermissionAsync(Caller caller, Role role, Scope scope, bool forNewUser)
    {
        if (!await caller.OutranksAsync(role))
            throw ApiException.Forbidden("You can only assign roles below your own.");
        if ((forNewUser || role.IsStudent) && !await caller.CanAsync(Capability.ManageUsers, scope))
            throw ApiException.Forbidden("You can't manage users in that department, class or batch.");
        if (!role.IsStudent && !await caller.CanAsync(Capability.AssignRoles, scope))
            throw ApiException.Forbidden("You can't assign roles in that department, class or batch.");
    }

    public static object Snapshot(RoleAssignment a) =>
        new { a.UserId, Role = a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo };

    private static async Task EnsureScopeExistsAsync(AppDbContext db, Scope s)
    {
        var v = new Validator();
        if (s.BranchId is int b) v.Rule(await db.Branches.AnyAsync(x => x.Id == b), "branchId", "That department doesn't exist.");
        if (s.ClassId is int c) v.Rule(await db.Classes.AnyAsync(x => x.Id == c), "classId", "That class doesn't exist.");
        if (s.BatchId is int t) v.Rule(await db.Batches.AnyAsync(x => x.Id == t), "batchId", "That batch doesn't exist.");
        v.ThrowIfInvalid();
    }
}
