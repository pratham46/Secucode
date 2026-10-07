using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Users;
using Secucode.Contracts;

namespace Secucode.Api.Features.Roles;

public static class AssignmentEndpoints
{
    public static void MapAssignments(this IEndpointRouteBuilder api)
    {
        api.MapGet("/users/{id:int}/assignments", ListAsync);
        api.MapPost("/users/{id:int}/assignments", AddAsync);
        api.MapPut("/assignments/{id:int}", UpdateAsync);
        api.MapDelete("/assignments/{id:int}", DeleteAsync);
    }

    private static async Task<List<AssignmentDto>> ListAsync(int id, AppDbContext db, Caller caller)
    {
        var user = await UserEndpoints.FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (!await caller.CanManageUserAsync(Capability.ManageUsers, user) && !await caller.CanManageUserAsync(Capability.AssignRoles, user))
            throw ApiException.NotFound("User");
        return user.Assignments.Select(a => a.ToDto(caller.Today)).ToList();
    }

    private static async Task<IResult> AddAsync(int id, AssignmentRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await UserEndpoints.FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (user.Id == caller.UserId)
            throw ApiException.Forbidden("You can't change your own roles.");
        if (!await caller.OutranksUserAsync(user))
            throw ApiException.Forbidden("You can't change roles for someone at or above your rank.");

        var assignment = await AssignmentRules.BuildAsync(db, caller, req, user.Assignments, user.PinHash is not null, forNewUser: false, time.UtcNow());

        await using var tx = await db.Database.BeginTransactionAsync();
        user.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "assignment.create", "RoleAssignment", assignment.Id, after: AssignmentRules.Snapshot(assignment), branchId: assignment.BranchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/assignments/{assignment.Id}", assignment.ToDto(caller.Today));
    }

    private static async Task<AssignmentDto> UpdateAsync(int id, UpdateAssignmentRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var a = await EditableAsync(db, caller, id);
        new Validator()
            .Rule(req.ValidFrom is null || req.ValidTo is null || req.ValidTo >= req.ValidFrom, "validTo", "End date must be on or after the start date.")
            .Rule(!a.Role.IsSystem || req.ValidTo is null, "validTo", "Super Admin assignments can't have an end date.")
            .ThrowIfInvalid();

        var before = AssignmentRules.Snapshot(a);
        a.ValidFrom = req.ValidFrom;
        a.ValidTo = req.ValidTo;
        audit.Record(caller.UserId, "assignment.update", "RoleAssignment", a.Id, before, AssignmentRules.Snapshot(a), branchId: a.BranchId);
        await db.SaveChangesAsync();
        return a.ToDto(caller.Today);
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var a = await EditableAsync(db, caller, id);
        var before = AssignmentRules.Snapshot(a);
        db.RoleAssignments.Remove(a);
        audit.Record(caller.UserId, "assignment.delete", "RoleAssignment", a.Id, before, branchId: a.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<RoleAssignment> EditableAsync(AppDbContext db, Caller caller, int id)
    {
        var a = await db.RoleAssignments.Include(x => x.Role)
            .Include(x => x.User).ThenInclude(u => u.Assignments).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Assignment");
        if (a.UserId == caller.UserId)
            throw ApiException.Forbidden("You can't change your own roles.");
        if (!await caller.OutranksUserAsync(a.User))
            throw ApiException.Forbidden("You can't change roles for someone at or above your rank.");
        await AssignmentRules.CheckPermissionAsync(caller, a.Role, a.Scope, forNewUser: false);
        return a;
    }
}
