using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Contracts;

namespace Secucode.Api.Auth;

// The signed-in user for this request. Assignments are read from the database once per request,
// so role changes take effect immediately (spec §5.3).
public sealed class Caller(AppDbContext db, TimeProvider time)
{
    private IReadOnlyList<RoleAssignment>? _active;

    public ClaimsPrincipal Principal { get; set; } = new();

    public int UserId => int.TryParse(Principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
        ? id
        : throw ApiException.Unauthorized("You need to sign in.");

    public ClientKind Client => Enum.TryParse<ClientKind>(Principal.FindFirstValue(TokenService.ClientClaim), out var c) ? c : ClientKind.Web;

    public DateOnly Today => time.Today();

    public async Task<IReadOnlyList<RoleAssignment>> ActiveAssignmentsAsync()
    {
        if (_active is not null)
            return _active;
        var today = Today;
        var userId = UserId;
        _active = await db.RoleAssignments.AsNoTracking()
            .Include(a => a.Role).ThenInclude(r => r.Capabilities)
            .Where(a => a.UserId == userId && a.User.IsActive
                && (a.ValidFrom == null || a.ValidFrom <= today)
                && (a.ValidTo == null || a.ValidTo >= today))
            .ToListAsync();
        return _active;
    }

    public async Task<bool> IsStaffAsync() => (await ActiveAssignmentsAsync()).Any(a => !a.Role.IsStudent);

    public async Task<Scope?> StudentScopeAsync() =>
        (await ActiveAssignmentsAsync()).Where(a => a.Role.IsStudent).Select(a => (Scope?)a.Scope).FirstOrDefault();

    public async Task<bool> IsSuperAdminAsync() => (await ActiveAssignmentsAsync()).Any(a => a.Role.IsSystem);

    public async Task<int> HighestRankAsync()
    {
        var active = await ActiveAssignmentsAsync();
        return active.Count == 0 ? -1 : active.Max(a => a.Role.Rank);
    }

    public async Task<bool> HasAnywhereAsync(Capability cap) =>
        (await ActiveAssignmentsAsync()).Any(a => a.Role.Capabilities.Any(c => c.Capability == cap));

    // Spec §5.3 step 2.
    public async Task<bool> CanAsync(Capability cap, Scope target, int? examCreatorId = null)
    {
        foreach (var a in await ActiveAssignmentsAsync())
        foreach (var grant in a.Role.Capabilities.Where(c => c.Capability == cap))
        {
            if (grant.Reach == Reach.InScope && a.Scope.Contains(target))
                return true;
            if (grant.Reach == Reach.OwnOnly && examCreatorId is not null && examCreatorId == UserId)
                return true;
        }
        return false;
    }

    // Spec §5.3 step 3: creators can always manage and view their own exam; everything else comes from the grid.
    public async Task<bool> CanOnExamAsync(Capability cap, Exam exam) =>
        (exam.CreatedBy == UserId && cap is Capability.ManageExams or Capability.ViewResults)
        || await CanAsync(cap, exam.Target, exam.CreatedBy);

    // Spec §5.5: lower rank only, except Super Admins may handle the Super Admin role.
    public async Task<bool> OutranksAsync(Role role) =>
        role.Rank < await HighestRankAsync() || (role.IsSystem && await IsSuperAdminAsync());

    // Nobody manages themselves; every role the target holds must be outranked. Target needs Assignments + Role loaded.
    public async Task<bool> OutranksUserAsync(User target)
    {
        if (target.Id == UserId)
            return false;
        foreach (var a in target.Assignments)
            if (!await OutranksAsync(a.Role))
                return false;
        return true;
    }

    public async Task<bool> CanManageUserAsync(Capability cap, User target)
    {
        if (!await OutranksUserAsync(target))
            return false;
        if (target.Assignments.Count == 0)
            return await CanAsync(cap, Scope.Institution);
        foreach (var a in target.Assignments)
            if (await CanAsync(cap, a.Scope))
                return true;
        return false;
    }

    public async Task RequireAsync(Capability cap, Scope target, string message)
    {
        if (!await CanAsync(cap, target))
            throw ApiException.Forbidden(message);
    }

    public Task RequireInstitutionAsync(Capability cap) =>
        RequireAsync(cap, Scope.Institution, $"You need the {cap} permission for the whole institution.");

    public async Task RequireOnExamAsync(Capability cap, Exam exam, string message)
    {
        if (!await CanOnExamAsync(cap, exam))
            throw ApiException.Forbidden(message);
    }

    public async Task RequireStaffAsync()
    {
        if (!await IsStaffAsync())
            throw ApiException.Forbidden("Only staff can do this.");
    }
}
