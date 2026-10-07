using Microsoft.EntityFrameworkCore;
using Noto.Server.Auth;
using Noto.Server.Data;

namespace Noto.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this RouteGroupBuilder v1)
    {
        var auth = v1.MapGroup("/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (AuthRequest req, AuthService svc, CancellationToken ct) =>
            Results.Json(await svc.RegisterAsync(req, ct), statusCode: StatusCodes.Status201Created));

        auth.MapPost("/login", async (AuthRequest req, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.LoginAsync(req, ct)));

        auth.MapPost("/refresh", async (RefreshBody body, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.RefreshAsync(body.RefreshToken, ct)));

        auth.MapPost("/logout", async (HttpContext http, AuthService svc, CancellationToken ct) =>
        {
            await svc.LogoutAsync(http.User.UserId(), http.User.DeviceId(), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        var devices = v1.MapGroup("/devices").RequireAuthorization();

        devices.MapGet("", async (HttpContext http, ServerDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.UserId();
            var list = await db.Devices.Where(d => d.UserId == userId && d.RevokedAt == null).OrderBy(d => d.CreatedAt)
                .Select(d => new { id = d.Id, name = d.Name, platform = d.Platform, last_sync_at = d.LastSyncAt, cursor = d.Cursor })
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        devices.MapDelete("/{id:guid}", async (Guid id, HttpContext http, AuthService svc, CancellationToken ct) =>
        {
            await svc.RevokeDeviceAsync(http.User.UserId(), id, ct);
            return Results.NoContent();
        });
    }

    public sealed record RefreshBody([property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken);
}
