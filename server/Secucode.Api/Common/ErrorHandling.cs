using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Secucode.Contracts;

namespace Secucode.Api.Common;

public static class ErrorHandling
{
    public static IApplicationBuilder UseApiErrors(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex)
        {
            await WriteAsync(ctx, ex.Status, ex.Title, ex.Detail, ex.Errors);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // e.g. two simultaneous "start attempt" calls racing for the same attempt number
            await WriteAsync(ctx, 409, "Conflict", "That conflicts with an existing record. Refresh and try again.", null);
        }
    });

    public static Task WriteAsync(HttpContext ctx, int status, string title, string? detail, IDictionary<string, string[]>? errors)
    {
        ProblemDetails problem = errors is null ? new ProblemDetails() : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? ctx.TraceIdentifier;
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync<object>(problem, Json.Options, "application/problem+json");
    }
}
