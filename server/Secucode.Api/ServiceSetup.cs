using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Secucode.Api.Auth;
using Secucode.Api.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using Secucode.Api.Data;

namespace Secucode.Api;

public static class ServiceSetup
{
    public static IServiceCollection AddSecucodeApi(this IServiceCollection services, IConfiguration config)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddProblemDetails();
        services.AddDbContext<AppDbContext>(o => o.UseMySql(config.GetConnectionString("Default"), DatabaseSetup.ServerVersion));
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(config.GetSection("Cors:WebOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            });
        });
        services.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt"))
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Jwt:Key must be at least 32 bytes.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((o, jwt, time) =>
            {
                o.MapInboundClaims = false; // keep "sub" as "sub"
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key)),
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    // Use the injected clock so tests can move time.
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = time.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore.Value <= now.AddMinutes(1)) && (expires is not null && expires.Value > now);
                    },
                };
                o.Events = new JwtBearerEvents
                {
                    // SignalR sends the token in the query string for WebSockets.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddSingleton<TokenService>();
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<Caller>();
        services.AddScoped<AuditWriter>();
        return services;
    }

    public static WebApplication UseSecucodeApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseApiErrors();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (ctx, next) =>
        {
            ctx.RequestServices.GetRequiredService<Caller>().Principal = ctx.User;
            await next(ctx);
        });
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.MapSecucodeEndpoints();
        return app;
    }
}
