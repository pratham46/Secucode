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
}
