using Noto.Server.Auth;
using Noto.Server.Middleware;
using Noto.Server.Sync;
using Noto.Sync;

namespace Noto.Server.Endpoints;

public static class SyncEndpoints
{
    public static void MapSync(this RouteGroupBuilder v1)
    {
        var sync = v1.MapGroup("/sync").RequireAuthorization();

        sync.MapPost(
                "",
                async (SyncRequest req, HttpContext http, SyncService svc, CancellationToken ct) =>
                {
                    // The token is bound to one device; ops must not be pushed under another device's id.
                    if (req.DeviceId != http.User.DeviceId())
                        throw ApiException.Forbidden(
                            "DEVICE_MISMATCH",
                            "device_id does not match the token"
                        );
                    return Results.Ok(
                        await svc.SyncAsync(http.User.UserId(), req.DeviceId, req, ct)
                    );
                }
            )
            .RequireRateLimiting("sync");

        sync.MapGet(
                "/snapshot",
                async (
                    Guid workspace_id,
                    string entity_type,
                    string? after,
                    int? limit,
                    HttpContext http,
                    SyncService svc,
                    CancellationToken ct
                ) =>
                    Results.Ok(
                        await svc.SnapshotAsync(
                            http.User.UserId(),
                            workspace_id,
                            entity_type,
                            after,
                            limit ?? 500,
                            ct
                        )
                    )
            )
            .RequireRateLimiting("snapshot");

        // Not in the original API sketch: a new device needs to learn which workspaces exist before it can snapshot them.
        sync.MapGet(
            "/workspaces",
            async (HttpContext http, SyncService svc, CancellationToken ct) =>
                Results.Ok(
                    new ServerWorkspaces(await svc.ListWorkspacesAsync(http.User.UserId(), ct))
                )
        );

        sync.MapDelete(
            "/workspaces/{id:guid}",
            async (Guid id, HttpContext http, SyncService svc, CancellationToken ct) =>
            {
                await svc.DeleteWorkspaceAsync(http.User.UserId(), id, ct);
                return Results.NoContent();
            }
        );
    }
}
