using System.Text.Json.Serialization;
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
        return services;
    }

    public static WebApplication UseSecucodeApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors();
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.MapSecucodeEndpoints();
        return app;
    }
}
