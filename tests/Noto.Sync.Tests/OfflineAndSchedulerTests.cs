using Microsoft.Extensions.Time.Testing;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Sync.Tests.Support;

namespace Noto.Sync.Tests;

public sealed class OfflineTests : IDisposable
{
    readonly TestServer _server = new();
    readonly Replica _a;
    readonly Replica _b;

    public OfflineTests()
    {
        var user = _server.RegisterUser();
        _a = new Replica(_server, user, TimeSpan.Zero);
        _b = new Replica(_server, user, TimeSpan.Zero);
    }

    public void Dispose() { _a.Dispose(); _b.Dispose(); _server.Dispose(); }

    [Fact]
    public async Task The_app_works_fully_offline_with_sync_enabled_and_catches_up_later()
    {
        var ws = await _a.AddWorkspaceAsync();
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.Client.SyncAsync();
        _a.Transport.Online = false;

        var id = await _a.AddItemAsync(ws.Id, "made offline");
        await _a.Bus.SendAsync(new CompleteItem(id));
        await _a.Bus.SendAsync(new RenameItem(id, "renamed offline"));
        await Should.ThrowAsync<SyncTransportException>(() => _a.Client.SyncAsync());

        (await _a.ItemAsync(id))!.Status.ShouldBe(ItemStatus.Done); // local state is intact
        (await _a.PendingAsync()).ShouldNotBeEmpty();

        _a.Transport.Online = true;
        await _a.Client.SyncAsync();
        await _b.Workspaces.BootstrapNewDeviceAsync();

        (await _a.PendingAsync()).ShouldBeEmpty();
        (await _b.ItemAsync(id))!.Title.ShouldBe("renamed offline");
    }

    [Fact]
    public async Task A_failed_sync_changes_nothing_locally_and_is_safely_retried()
    {
        var ws = await _a.AddWorkspaceAsync();
        await _a.Workspaces.EnableAsync(ws.Id);
        await _a.AddItemAsync(ws.Id);
        var pendingBefore = (await _a.PendingAsync()).Count;
        _a.Transport.Online = false;

        await Should.ThrowAsync<SyncTransportException>(() => _a.Client.SyncAsync());

        (await _a.PendingAsync()).Count.ShouldBe(pendingBefore);
    }
}

public sealed class SchedulerTests : IDisposable
{
    readonly TestServer _server = new();
    readonly Replica _a;
    readonly FakeTimeProvider _time = new();
    readonly Guid _ws;

    public SchedulerTests()
    {
        _a = new Replica(_server, _server.RegisterUser(), TimeSpan.Zero);
        var ws = _a.AddWorkspaceAsync().GetAwaiter().GetResult();
        _ws = ws.Id;
        _a.Workspaces.EnableAsync(ws.Id).GetAwaiter().GetResult();
    }

    public void Dispose() { _a.Dispose(); _server.Dispose(); }

    static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        condition().ShouldBeTrue();
    }

    [Fact]
    public async Task Changes_are_debounced_two_seconds_then_synced()
    {
        await using var scheduler = new SyncScheduler(_a.Client, _time);
        await _a.AddItemAsync(_ws);

        scheduler.NotifyChanged();
        _time.Advance(TimeSpan.FromSeconds(1));
        scheduler.NotifyChanged();            // a second change restarts the window
        _time.Advance(TimeSpan.FromSeconds(1.5));
        (await _a.PendingAsync()).ShouldNotBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => _a.PendingAsync().GetAwaiter().GetResult().Count == 0);
    }

    [Fact]
    public async Task Foreground_loop_syncs_every_minute()
    {
        await using var scheduler = new SyncScheduler(_a.Client, _time);
        scheduler.Start();
        await _a.AddItemAsync(_ws);

        _time.Advance(TimeSpan.FromSeconds(59));
        (await _a.PendingAsync()).ShouldNotBeEmpty();
        _time.Advance(TimeSpan.FromSeconds(2));

        await Eventually(() => _a.PendingAsync().GetAwaiter().GetResult().Count == 0);
    }

    [Fact]
    public async Task Failures_become_an_offline_status_and_never_throw_to_the_app()
    {
        await using var scheduler = new SyncScheduler(_a.Client, _time);
        var seen = new List<SyncStatus>();
        scheduler.StatusChanged += seen.Add;
        _a.Transport.Online = false;
        await _a.AddItemAsync(_ws);

        await scheduler.RunAsync();

        scheduler.Status.ShouldBe(SyncStatus.Offline);
        scheduler.LastError.ShouldBeOfType<SyncTransportException>();

        _a.Transport.Online = true;
        scheduler.NotifyReconnected();
        await Eventually(() => scheduler.Status == SyncStatus.Idle);
        seen.ShouldContain(SyncStatus.Offline);
    }
}
