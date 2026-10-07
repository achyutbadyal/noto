using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Noto.Server.Config;

namespace Noto.Server.Auth;

public sealed class JwtService(ServerConfig config, TimeProvider time)
{
    public const string Audience = "noto";
    public const string DeviceClaim = "did";
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);

    public static SymmetricSecurityKey Key(ServerConfig c) =>
        new(Encoding.UTF8.GetBytes(c.JwtSigningKey));

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(Guid userId, Guid deviceId)
    {
        var now = time.GetUtcNow();
        var expires = now + AccessLifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = config.PublicUrl.GetLeftPart(UriPartial.Authority),
            Audience = Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new System.Security.Claims.ClaimsIdentity([
                new System.Security.Claims.Claim("sub", userId.ToString()),
                new System.Security.Claims.Claim(DeviceClaim, deviceId.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(Key(config), SecurityAlgorithms.HmacSha256),
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
