using Secucode.Api.Auth;
using Secucode.Api.Features.Masters;
using Secucode.Api.Features.Roles;
using Secucode.Api.Features.Users;

namespace Secucode.Api;

public static class Endpoints
{
    public static void MapSecucodeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        api.MapAuth();
        api.MapMasters();
        api.MapSettings();
        api.MapUsers();
        api.MapRoles();
        api.MapAssignments();
    }
}
