using Microsoft.Extensions.Time.Testing;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Core.Time;
using Noto.Data;

namespace Noto.Sync.Tests.Support;

// Reads the shared fake time plus a fixed skew, to model devices whose clocks disagree.
public sealed class SkewedClock(FakeTimeProvider time, TimeSpan skew) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow() + skew;
    public TimeZoneInfo DeviceTimeZone => TimeZoneInfo.Utc;
}

// One device: its own SQLite database, command bus, HLC and sync client.
public sealed class Replica : IDisposable
{
    public Guid DeviceId { get; } = Guid.CreateVersion7();
    public SqliteUnitOfWork Db { get; }
    public CommandBus Bus { get; }
    public InProcessTransport Transport { get; }
    public SyncClient Client { get; }
    public WorkspaceSyncService Workspaces { get; }
    public HybridClock Hlc { get; }

    public Replica(TestServer server, Guid userId, TimeSpan skew, SyncOptions? options = null)
    {
        server.RegisterDevice(userId, DeviceId);
        var clock = new SkewedClock(server.Time, skew);
        Hlc = new HybridClock(() => clock.UtcNow.ToUnixTimeMilliseconds(), DeviceId);
        Db = SqliteUnitOfWork.InMemory(Hlc); // records ops for every synced write, whoever makes it
        Bus = new CommandBus(Db, clock, DeviceId, Hlc);
        Transport = new InProcessTransport(server, userId, DeviceId);
        var merger = new Merger(EntityCodecs.Default, Hlc, server.Time);
        Workspaces = new WorkspaceSyncService(Db, Transport, merger, DeviceId, options);
        Client = new SyncClient(Db, Transport, merger, Workspaces, Hlc, DeviceId, options);
    }

    public async Task<Workspace> AddWorkspaceAsync(Guid? id = null, string name = "Work")
    {
        var ws = new Workspace
        {
            Id = id ?? Guid.CreateVersion7(), Name = name, TimeZone = "UTC", TzFollowsDevice = false,
            CreatedAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        };
        await Bus.SaveWorkspaceAsync(ws);
        return ws;
    }

    public async Task<Guid> AddItemAsync(Guid workspaceId, string title = "Task", DateOnly? planned = null)
    {
        var id = Guid.CreateVersion7();
        await Bus.SendAsync(new CreateItem(id, workspaceId, title, planned ?? new DateOnly(2026, 10, 7)));
        return id;
    }

    public Task<TodoItem?> ItemAsync(Guid id) => Db.RunAsync(s => s.Items.GetAsync(id));
    public Task<IReadOnlyList<Op>> PendingAsync() => Db.RunAsync(s => s.Sync.ListPendingAsync(100_000));

    public void Dispose() => Db.Dispose();
}
