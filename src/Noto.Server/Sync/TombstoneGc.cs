using Microsoft.EntityFrameworkCore;
using Noto.Core.Sync;
using Noto.Server.Data;

namespace Noto.Server.Sync;

// Tombstones are kept for 90 days, and only until every active device of the account has pulled past them.
public sealed class TombstoneGc(ServerDbContext db, TimeProvider time)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - Retention;
        var removed = 0;

        var candidates = await db.CurrentRows
            .Where(r => r.EntityType == EntityTypes.TodoItem && r.DeletedAt != null && r.DeletedAt < cutoff)
            .ToListAsync(ct);

        foreach (var byUser in candidates.GroupBy(r => r.UserId))
        {
            var cursors = await db.Devices.Where(d => d.UserId == byUser.Key && d.RevokedAt == null).Select(d => d.Cursor).ToListAsync(ct);
            if (cursors.Count == 0) continue;
            var safeSeq = cursors.Min();

            foreach (var item in byUser.Where(r => r.LastSeq <= safeSeq))
            {
                await db.Ops.Where(o => o.EntityId == item.EntityId && o.Seq <= safeSeq).ExecuteDeleteAsync(ct);
                var events = await db.CurrentRows.Where(r => r.EntityType == EntityTypes.ItemEvent && r.RefId == item.EntityId && r.LastSeq <= safeSeq)
                    .Select(r => r.EntityId).ToListAsync(ct);
                await db.Ops.Where(o => o.EntityType == EntityTypes.ItemEvent && events.Contains(o.EntityId) && o.Seq <= safeSeq).ExecuteDeleteAsync(ct);
                await db.CurrentRows.Where(r => r.EntityType == EntityTypes.ItemEvent && events.Contains(r.EntityId)).ExecuteDeleteAsync(ct);
                db.CurrentRows.Remove(item);
                removed++;
            }
        }
        await db.SaveChangesAsync(ct);
        return removed;
    }
}

public sealed class TombstoneGcService(IServiceScopeFactory scopes, ILogger<TombstoneGcService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<TombstoneGc>().RunAsync(stoppingToken);
                if (removed > 0) log.LogInformation("Tombstone GC removed {Count} items", removed);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Tombstone GC failed: {Type}", e.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
