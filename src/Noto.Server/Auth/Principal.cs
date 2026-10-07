using System.Security.Claims;

namespace Noto.Server.Auth;

public static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue("sub") ?? throw new InvalidOperationException("No sub claim"));

    public static Guid DeviceId(this ClaimsPrincipal p) =>
        Guid.Parse(
            p.FindFirstValue(JwtService.DeviceClaim)
                ?? throw new InvalidOperationException("No device claim")
        );
}
