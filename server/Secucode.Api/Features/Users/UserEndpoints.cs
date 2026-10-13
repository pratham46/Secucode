using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Features.Users;

public static partial class UserEndpoints
{
    private const int PageSize = 50;

    public static void MapUsers(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/users");
        g.MapGet("", ListAsync);
        g.MapPost("", CreateAsync);
        g.MapGet("/{id:int}", GetAsync);
        g.MapPut("/{id:int}", UpdateAsync);
        g.MapDelete("/{id:int}", DeactivateAsync);
        g.MapPost("/{id:int}/reset-password", ResetPasswordAsync);
        g.MapPost("/{id:int}/reset-pin", ResetPinAsync);
    }

    public static Task<User?> FindAsync(AppDbContext db, int id) =>
        db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).SingleOrDefaultAsync(u => u.Id == id);

    private static async Task<PageDto<UserDto>> ListAsync(AppDbContext db, Caller caller, int? roleId, int? branchId, string? search, int? page)
    {
        if (!await caller.HasAnywhereAsync(Capability.ManageUsers) && !await caller.HasAnywhereAsync(Capability.AssignRoles))
            throw ApiException.Forbidden("You can't view users.");

        var q = db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).AsNoTracking();
        if (roleId is not null) q = q.Where(u => u.Assignments.Any(a => a.RoleId == roleId));
        if (branchId is not null) q = q.Where(u => u.Assignments.Any(a => a.BranchId == branchId));
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => u.Username.Contains(search) || u.FullName.Contains(search));

        // ponytail: visibility is filtered in memory; fine for one college, push into SQL if users reach tens of thousands
        var visible = new List<User>();
        foreach (var u in await q.OrderBy(u => u.Username).ToListAsync())
            if (await caller.CanManageUserAsync(Capability.ManageUsers, u) || await caller.CanManageUserAsync(Capability.AssignRoles, u))
                visible.Add(u);

        var p = Math.Max(page ?? 1, 1);
        var items = visible.Skip((p - 1) * PageSize).Take(PageSize).Select(u => u.ToDto(caller.Today)).ToList();
        return new PageDto<UserDto>(items, visible.Count, p, PageSize);
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var username = req.Username?.Trim() ?? "";
        new Validator()
            .Rule(UsernamePattern().IsMatch(username), "username", "Username must be 3-100 letters, digits, dots, dashes or underscores.")
            .Rule((req.Password?.Length ?? 0) >= 6, "password", "Password must be at least 6 characters.")
            .Rule(!string.IsNullOrWhiteSpace(req.FullName) && req.FullName.Trim().Length <= 200, "fullName", "Full name is required (up to 200 characters).")
            .Rule(ValidEmail(req.Email), "email", "That email address isn't valid.")
            .Rule(req.Pin is null || PinPattern().IsMatch(req.Pin), "pin", "PIN must be 4 to 6 digits.")
            .Rule(req.Assignments is { Count: > 0 }, "assignments", "Give the user at least one role.")
            .ThrowIfInvalid();
        if (await db.Users.AnyAsync(u => u.Username == username))
            throw ApiException.Conflict($"The username '{username}' is taken.");

        var now = time.UtcNow();
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            PinHash = req.Pin is null ? null : BCrypt.Net.BCrypt.HashPassword(req.Pin),
            FullName = req.FullName!.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            CreatedAt = now,
        };
        foreach (var a in req.Assignments!)
            user.Assignments.Add(await AssignmentRules.BuildAsync(db, caller, a, user.Assignments, user.PinHash is not null, forNewUser: true, now));

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        audit.Record(caller.UserId, "user.create", "User", user.Id, after: Snapshot(user), branchId: user.Assignments[0].BranchId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/users/{user.Id}", user.ToDto(caller.Today));
    }

    private static async Task<UserDto> GetAsync(int id, AppDbContext db, Caller caller)
    {
        var user = await FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (!await caller.CanManageUserAsync(Capability.ManageUsers, user) && !await caller.CanManageUserAsync(Capability.AssignRoles, user))
            throw ApiException.NotFound("User");
        return user.ToDto(caller.Today);
    }

    private static async Task<UserDto> UpdateAsync(int id, UpdateUserRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await ManageableAsync(db, caller, id);
        new Validator()
            .Rule(!string.IsNullOrWhiteSpace(req.FullName) && req.FullName.Trim().Length <= 200, "fullName", "Full name is required (up to 200 characters).")
            .Rule(ValidEmail(req.Email), "email", "That email address isn't valid.")
            .ThrowIfInvalid();

        var before = Snapshot(user);
        var deactivating = user.IsActive && !req.IsActive;
        user.FullName = req.FullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        user.IsActive = req.IsActive;
        await using var tx = await db.Database.BeginTransactionAsync();
        audit.Record(caller.UserId, "user.update", "User", user.Id, before, Snapshot(user), branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        if (deactivating) await RevokeSessionsAsync(db, user.Id, time);
        await tx.CommitAsync();
        return user.ToDto(caller.Today);
    }

    private static async Task<IResult> DeactivateAsync(int id, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await ManageableAsync(db, caller, id);
        var before = Snapshot(user);
        user.IsActive = false;
        await using var tx = await db.Database.BeginTransactionAsync();
        audit.Record(caller.UserId, "user.deactivate", "User", user.Id, before, Snapshot(user), branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        await RevokeSessionsAsync(db, user.Id, time);
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(int id, ResetPasswordRequest req, AppDbContext db, Caller caller, AuditWriter audit, TimeProvider time)
    {
        var user = await ManageableAsync(db, caller, id);
        new Validator().Rule((req.NewPassword?.Length ?? 0) >= 6, "newPassword", "Password must be at least 6 characters.").ThrowIfInvalid();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await using var tx = await db.Database.BeginTransactionAsync();
        audit.Record(caller.UserId, "user.reset_password", "User", user.Id, branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        await RevokeSessionsAsync(db, user.Id, time);
        await tx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPinAsync(int id, ResetPinRequest req, AppDbContext db, Caller caller, AuditWriter audit)
    {
        var user = await ManageableAsync(db, caller, id);
        new Validator().Rule(req.NewPin is not null && PinPattern().IsMatch(req.NewPin), "newPin", "PIN must be 4 to 6 digits.").ThrowIfInvalid();
        user.PinHash = BCrypt.Net.BCrypt.HashPassword(req.NewPin);
        audit.Record(caller.UserId, "user.reset_pin", "User", user.Id, branchId: user.Assignments.FirstOrDefault()?.BranchId);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<User> ManageableAsync(AppDbContext db, Caller caller, int id)
    {
        var user = await FindAsync(db, id) ?? throw ApiException.NotFound("User");
        if (!await caller.CanManageUserAsync(Capability.ManageUsers, user))
            throw ApiException.Forbidden("You can't manage this user.");
        return user;
    }

    private static Task RevokeSessionsAsync(AppDbContext db, int userId, TimeProvider time)
    {
        var now = time.UtcNow();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }

    private static object Snapshot(User u) => new
    {
        u.Username, u.FullName, u.Email, u.IsActive,
        Roles = u.Assignments.Select(a => new { Role = a.Role.Name, a.BranchId, a.ClassId, a.BatchId, a.ValidFrom, a.ValidTo }).ToList(),
    };

    private static bool ValidEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) || (email.Contains('@') && email.Trim().Length <= 200);

    [GeneratedRegex("^[A-Za-z0-9._-]{3,100}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex PinPattern();
}
