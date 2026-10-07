using System.Net.Http.Json;
using System.Text.Json;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Sync;
using Noto.Core.Time;
using Noto.Data;
using Noto.Sync;

namespace Noto.Server.Tests;

// Two real clients (own SQLite, bus, HLC, SyncClient) syncing through the actual HTTP API, auth included.
public sealed class HttpEndToEndTests : IDisposable
{
    readonly ServerFactory _f = new();
    public void Dispose() => _f.Dispose();

    sealed class Clock(Func<DateTimeOffset> now) : IClock
    {
        public DateTimeOffset UtcNow => now();
        public TimeZoneInfo DeviceTimeZone => TimeZoneInfo.Utc;
    }

    sealed class Device : IDisposable
    {
        public Guid Id = Guid.NewGuid();
        public required SqliteUnitOfWork Db;
        public required CommandBus Bus;
        public required SyncClient Client;
        public required WorkspaceSyncService Workspaces;
        public void Dispose() => Db.Dispose();
    }

    Device NewDevice(HttpClient http, string email, Guid id)
    {
        string? token = null, refresh = null;
        async Task LoginAsync(CancellationToken ct)
        {
            var r = await http.PostAsJsonAsync("/v1/auth/login", new { email, password = "correct horse battery", device = ServerFactory.Device(id) }, ct);
            var json = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
            token = json.GetProperty("access_token").GetString();
            refresh = json.GetProperty("refresh_token").GetString();
        }

        var clock = new Clock(() => _f.Time.GetUtcNow());
        var hlc = new HybridClock(() => clock.UtcNow.ToUnixTimeMilliseconds(), id);
        var db = SqliteUnitOfWork.InMemory(hlc);
        var transport = new HttpSyncTransport(http,
            async ct => { if (token is null) await LoginAsync(ct); return token; },
            async ct =>
            {
                var r = await http.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = refresh }, ct);
                if (!r.IsSuccessStatusCode) return false;
                var json = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
                token = json.GetProperty("access_token").GetString();
                refresh = json.GetProperty("refresh_token").GetString();
                return true;
            });
        var merger = new Merger(EntityCodecs.Default, hlc, _f.Time);
        var workspaces = new WorkspaceSyncService(db, transport, merger, id);
        return new Device
        {
            Id = id, Db = db, Bus = new CommandBus(db, clock, id, hlc), Workspaces = workspaces,
            Client = new SyncClient(db, transport, merger, workspaces, hlc, id),
        };
    }

    [Fact]
    public async Task Two_devices_sync_through_the_http_api_including_token_expiry_and_refresh()
    {
        var first = await _f.RegisterAsync("e2e@example.com");
        var http = _f.CreateClient();
        using var a = NewDevice(http, "e2e@example.com", first.DeviceId);
        using var b = NewDevice(http, "e2e@example.com", Guid.NewGuid());

        var ws = new Workspace { Id = Guid.NewGuid(), Name = "Work", TimeZone = "UTC", TzFollowsDevice = false, CreatedAt = _f.Time.GetUtcNow() };
        await a.Bus.SaveWorkspaceAsync(ws);
        var item = Guid.NewGuid();
        await a.Bus.SendAsync(new CreateItem(item, ws.Id, "Over HTTP", new DateOnly(2026, 10, 7)));
        await a.Workspaces.EnableAsync(ws.Id);
        await a.Client.SyncAsync();

        await b.Workspaces.BootstrapNewDeviceAsync();
        (await b.Db.RunAsync(s => s.Items.GetAsync(item)))!.Title.ShouldBe("Over HTTP");

        _f.Time.Advance(TimeSpan.FromMinutes(20)); // both access tokens expire; clients must refresh transparently
        await b.Bus.SendAsync(new CompleteItem(item));
        await b.Client.SyncAsync();
        await a.Client.SyncAsync();

        (await a.Db.RunAsync(s => s.Items.GetAsync(item)))!.Status.ShouldBe(ItemStatus.Done);
        (await a.Db.RunAsync(s => s.Events.ListForItemAsync(item))).Select(e => e.Type)
            .ShouldBe([ItemEventType.Created, ItemEventType.Completed]);
    }

    [Fact]
    public async Task A_revoked_device_stops_syncing()
    {
        var owner = await _f.RegisterAsync("revoke@example.com");
        var http = _f.CreateClient();
        var laptopId = Guid.NewGuid();
        using var laptop = NewDevice(http, "revoke@example.com", laptopId);
        var ws = new Workspace { Id = Guid.NewGuid(), Name = "W", TimeZone = "UTC", TzFollowsDevice = false, CreatedAt = _f.Time.GetUtcNow() };
        await laptop.Bus.SaveWorkspaceAsync(ws);
        await laptop.Workspaces.EnableAsync(ws.Id);
        await laptop.Client.SyncAsync();

        (await owner.Client.DeleteAsync($"/v1/devices/{laptopId}")).IsSuccessStatusCode.ShouldBeTrue();
        await laptop.Bus.SendAsync(new CreateItem(Guid.NewGuid(), ws.Id, "after revoke"));

        await Should.ThrowAsync<SyncTransportException>(() => laptop.Client.SyncAsync());
        (await laptop.Db.RunAsync(s => s.Sync.ListPendingAsync(10))).ShouldNotBeEmpty(); // nothing lost locally
    }
}
