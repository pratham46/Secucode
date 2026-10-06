namespace Secucode.Api;

public static class Endpoints
{
    public static void MapSecucodeEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
    }
}
