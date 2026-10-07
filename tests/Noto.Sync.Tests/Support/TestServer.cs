using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Noto.Core.Sync;
using Noto.Server.Data;
using Noto.Server.Middleware;
using Noto.Server.Sync;
using Noto.Sync;

namespace Noto.Sync.Tests.Support;

// The real server sync logic over an in-memory SQLite database, called in-process (no HTTP).
public sealed class TestServer : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-10-07T09:00:00Z"));

    public TestServer()
    {
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    public ServerDbContext NewDb() => new(new DbContextOptionsBuilder<ServerDbContext>().UseSqlite(_conn).Options);

    public Guid RegisterUser()
    {
        using var db = NewDb();
        var user = new User { Id = Guid.CreateVersion7(), Email = $"{Guid.NewGuid():N}@test", PasswordHash = "x", CreatedAt = Time.GetUtcNow().UtcDateTime };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    public void RegisterDevice(Guid userId, Guid deviceId)
    {
        using var db = NewDb();
        db.Devices.Add(new Device { Id = deviceId, UserId = userId, Name = "test", Platform = "test", CreatedAt = Time.GetUtcNow().UtcDateTime });
        db.SaveChanges();
    }

    public async Task<T> WithServiceAsync<T>(Func<SyncService, Task<T>> work)
    {
        await using var db = NewDb();
        return await work(new SyncService(db, Time, new ServerClock(Time)));
    }
}

// ISyncTransport bound to one device. `Online = false` simulates a partition.
public sealed class InProcessTransport(TestServer server, Guid userId, Guid deviceId) : ISyncTransport
{
    public bool Online { get; set; } = true;
    public int Calls { get; private set; }

    void Check()
    {
        Calls++;
        if (!Online) throw new SyncTransportException("offline");
    }

    public async Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken ct = default)
    {
        Check();
        try { return await server.WithServiceAsync(s => s.SyncAsync(userId, deviceId, request, ct)); }
        catch (ApiException e) when (e.Code == "CURSOR_AHEAD") { throw new CursorAheadException(); }
    }

    public Task<SnapshotPage> SnapshotAsync(Guid workspaceId, string entityType, string? after, int limit, CancellationToken ct = default)
    {
        Check();
        return server.WithServiceAsync(async s =>
        {
            try { return await s.SnapshotAsync(userId, workspaceId, entityType, after, limit, ct); }
            catch (ApiException e) when (e.Code == "WORKSPACE_NOT_FOUND")
            {
                // A workspace the server has never seen simply has an empty snapshot.
                return new SnapshotPage(0, entityType, [], null);
            }
        });
    }

    public Task<IReadOnlyList<Guid>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        Check();
        return server.WithServiceAsync(s => s.ListWorkspacesAsync(userId, ct));
    }

    public Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        Check();
        return server.WithServiceAsync(async s => { await s.DeleteWorkspaceAsync(userId, workspaceId, ct); return 0; });
    }
}
