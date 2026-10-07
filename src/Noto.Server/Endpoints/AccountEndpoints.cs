using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noto.Server.Auth;
using Noto.Server.Data;
using Noto.Server.Middleware;

namespace Noto.Server.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccount(this RouteGroupBuilder v1)
    {
        var account = v1.MapGroup("/account").RequireAuthorization().RequireRateLimiting("account");

        account.MapGet(
            "/export",
            async (HttpContext http, ServerDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var userId = http.User.UserId();
                var job = new ExportJob
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    CreatedAt = time.GetUtcNow().UtcDateTime,
                    Content = await BuildExportAsync(db, userId, ct),
                };
                db.Exports.Add(job);
                await db.SaveChangesAsync(ct);
                return Results.Accepted($"/v1/account/export/{job.Id}", new { export_id = job.Id });
            }
        );

        account.MapGet(
            "/export/{id:guid}",
            async (Guid id, HttpContext http, ServerDbContext db, CancellationToken ct) =>
            {
                var userId = http.User.UserId();
                var job =
                    await db.Exports.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId, ct)
                    ?? throw ApiException.NotFound("EXPORT_NOT_FOUND", "Export not found");
                return Results.Content(job.Content, "application/json");
            }
        );

        // Deletes server data and revokes all devices. Local data on devices is untouched.
        account.MapDelete(
            "",
            async (HttpContext http, ServerDbContext db, CancellationToken ct) =>
            {
                var userId = http.User.UserId();
                await db.Ops.Where(o => o.UserId == userId).ExecuteDeleteAsync(ct);
                await db.CurrentRows.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
                await db.WorkspaceSyncs.Where(w => w.UserId == userId).ExecuteDeleteAsync(ct);
                await db.Exports.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct);
                await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
                await db.Devices.Where(d => d.UserId == userId).ExecuteDeleteAsync(ct);
                await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
                return Results.NoContent();
            }
        );

        account.MapPut(
            "/digest",
            async (JsonElement prefs, HttpContext http, ServerDbContext db, CancellationToken ct) =>
            {
                var userId = http.User.UserId();
                await db
                    .Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(u => u.DigestPrefsJson, prefs.GetRawText()),
                        ct
                    );
                return Results.NoContent();
            }
        );
    }

    static async Task<string> BuildExportAsync(
        ServerDbContext db,
        Guid userId,
        CancellationToken ct
    )
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var rows = await db
            .CurrentRows.Where(r => r.UserId == userId)
            .OrderBy(r => r.EntityType)
            .ThenBy(r => r.EntityId)
            .ToListAsync(ct);

        var workspaces = new JsonArray();
        foreach (var ws in rows.GroupBy(r => r.WorkspaceId))
        {
            var types = new JsonObject();
            foreach (var type in ws.GroupBy(r => r.EntityType))
                types[type.Key] = new JsonArray(
                    type.Select(r => (JsonNode?)JsonNode.Parse(r.Row)).ToArray()
                );
            workspaces.Add(
                new JsonObject { ["workspace_id"] = ws.Key.ToString(), ["rows"] = types }
            );
        }
        return new JsonObject
        {
            ["email"] = user.Email,
            ["exported_at"] = DateTime.UtcNow.ToString("O"),
            ["workspaces"] = workspaces,
        }.ToJsonString();
    }
}
