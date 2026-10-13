using Secucode.Api.Auth;
using Secucode.Api.Features.Attempts;
using Secucode.Api.Features.Audit;
using Secucode.Api.Features.Corrections;
using Secucode.Api.Features.Exams;
using Secucode.Api.Features.Live;
using Secucode.Api.Features.Masters;
using Secucode.Api.Features.Roles;
using Secucode.Api.Features.Runner;
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
        api.MapExams();
        api.MapRunner();
        api.MapAttempts();
        api.MapCorrections();
        api.MapAudit();
        app.MapHub<ExamHub>("/hubs/exams");
    }
}
