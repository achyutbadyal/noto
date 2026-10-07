using Microsoft.EntityFrameworkCore;
using Noto.Server.Auth;
using Noto.Server.Data;

namespace Noto.Server.Middleware;

// A revoked device's access token may still be unexpired; refuse it immediately (403, docs/06).
public sealed class DeviceStatusMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ServerDbContext db)
    {
        if (ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirst(JwtService.DeviceClaim) is { } claim)
        {
            var id = Guid.Parse(claim.Value);
            var revoked = await db.Devices.Where(d => d.Id == id).Select(d => d.RevokedAt != null).FirstOrDefaultAsync(ctx.RequestAborted);
            if (revoked || !await db.Devices.AnyAsync(d => d.Id == id, ctx.RequestAborted))
            {
                var problem = ApiExceptionHandler.Make(403, "DEVICE_REVOKED", "Device revoked");
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsJsonAsync(problem, options: null, "application/problem+json", ctx.RequestAborted);
                return;
            }
        }
        await next(ctx);
    }
}
