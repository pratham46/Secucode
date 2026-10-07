using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Secucode.Api.Common;
using Secucode.Contracts;

namespace Secucode.Api.Auth;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    public const string ClientClaim = "client";

    public int RefreshTokenDays => options.Value.RefreshTokenDays;

    // Only identity + client go in the token; permissions are loaded per request (spec §5.3).
    public (string Token, DateTime ExpiresAt) CreateAccessToken(int userId, ClientKind client)
    {
        var o = options.Value;
        var now = time.UtcNow();
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClientClaim, client.ToString())],
            notBefore: now, expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static string NewRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
