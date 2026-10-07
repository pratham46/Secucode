using Microsoft.EntityFrameworkCore;
using Secucode.Api.Common;
using Secucode.Api.Data;
using Secucode.Api.Data.Entities;
using Secucode.Api.Features.Users;
using Secucode.Contracts;

namespace Secucode.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/auth");
        g.MapPost("/login", LoginAsync).AllowAnonymous();
        g.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        g.MapPost("/logout", LogoutAsync);
        g.MapGet("/me", MeAsync);
    }

    private static async Task<LoginResponse> LoginAsync(LoginRequest req, AppDbContext db, TokenService tokens, LoginThrottle throttle,
        AuditWriter audit, TimeProvider time)
    {
        new Validator()
            .Rule(!string.IsNullOrWhiteSpace(req.Username), "username", "Username is required.")
            .Rule(!string.IsNullOrEmpty(req.Password), "password", "Password is required.")
            .ThrowIfInvalid();

        var username = req.Username.Trim();
        if (throttle.IsLocked(username))
            throw ApiException.TooManyRequests("Too many failed attempts. Try again in a few minutes.");

        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username);
        if (user is null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            throttle.Fail(username);
            audit.Record(null, "auth.login_failed", "User", username);
            await db.SaveChangesAsync();
            throw ApiException.Unauthorized("Invalid username or password.");
        }

        throttle.Succeed(username);
        audit.Record(user.Id, "auth.login", "User", user.Id, after: new { req.Client });
        var response = await IssueAsync(db, tokens, time, user, req.Client, replacing: null);
        await db.SaveChangesAsync();
        return response;
    }

    private static async Task<LoginResponse> RefreshAsync(RefreshRequest req, AppDbContext db, TokenService tokens, TimeProvider time)
    {
        var hash = TokenService.Hash(req.RefreshToken ?? "");
        var now = time.UtcNow();
        var rt = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash)
            ?? throw ApiException.Unauthorized("Invalid refresh token.");

        if (rt.RevokedAt is not null)
        {
            // A rotated token came back: assume it was stolen and sign the user out everywhere.
            await db.RefreshTokens.Where(t => t.UserId == rt.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
            throw ApiException.Unauthorized("This session was already used. Please sign in again.");
        }
        if (rt.ExpiresAt <= now)
            throw ApiException.Unauthorized("Your session expired. Please sign in again.");

        var user = await db.Users.SingleAsync(u => u.Id == rt.UserId);
        if (!user.IsActive)
            throw ApiException.Unauthorized("This account is deactivated.");

        var response = await IssueAsync(db, tokens, time, user, rt.Client, replacing: rt);
        await db.SaveChangesAsync();
        return response;
    }

    private static async Task<IResult> LogoutAsync(RefreshRequest req, AppDbContext db, Caller caller, TimeProvider time)
    {
        var hash = TokenService.Hash(req.RefreshToken ?? "");
        var userId = caller.UserId;
        var rt = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId);
        if (rt is { RevokedAt: null })
        {
            rt.RevokedAt = time.UtcNow();
            await db.SaveChangesAsync();
        }
        return Results.NoContent();
    }

    private static async Task<MeResponse> MeAsync(Caller caller, AppDbContext db)
    {
        var userId = caller.UserId;
        var user = await db.Users.Include(u => u.Assignments).ThenInclude(a => a.Role).AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw ApiException.Unauthorized("This account is deactivated.");

        var capabilities = (await caller.ActiveAssignmentsAsync())
            .SelectMany(a => a.Role.Capabilities.Select(c => new EffectiveCapabilityDto(c.Capability, c.Reach, a.Scope.ToDto(), a.Role.Name)))
            .ToList();
        return new MeResponse(user.ToDto(caller.Today), capabilities);
    }

    private static async Task<LoginResponse> IssueAsync(AppDbContext db, TokenService tokens, TimeProvider time, User user,
        ClientKind client, RefreshToken? replacing)
    {
        var now = time.UtcNow();
        var (access, expires) = tokens.CreateAccessToken(user.Id, client);
        var raw = TokenService.NewRefreshToken();
        var refresh = new RefreshToken
        {
            UserId = user.Id, Client = client, TokenHash = TokenService.Hash(raw),
            CreatedAt = now, ExpiresAt = now.AddDays(tokens.RefreshTokenDays),
        };
        db.RefreshTokens.Add(refresh);
        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedBy = refresh;
        }

        await db.Entry(user).Collection(u => u.Assignments).Query().Include(a => a.Role).LoadAsync();
        return new LoginResponse(access, raw, expires, user.ToDto(time.Today()));
    }
}
