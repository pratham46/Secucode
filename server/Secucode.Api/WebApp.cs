namespace Secucode.Api;

// Serves the React app (clients/web, built into wwwroot) from the API's own origin.
// App routes such as /exams or /attempt/5 get index.html so the browser router takes over;
// /api, /hubs and /swagger are never rewritten, so unknown API paths still fail as API errors.
public static class WebApp
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' blob: data:; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'none'";

    public static WebApplication UseWebApp(this WebApplication app)
    {
        app.Use((ctx, next) =>
        {
            var path = ctx.Request.Path;
            if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/hubs") && !path.StartsWithSegments("/swagger")
                && !Path.HasExtension(path.Value)
                && app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
                ctx.Request.Path = "/index.html";
            return next(ctx);
        });
        app.UseStaticFiles(); // options (CSP header) come from AddSecucodeApi
        return app;
    }
}
