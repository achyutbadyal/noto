using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Tests;
using Noto.Data;
using Noto.Platform.Abstractions;

namespace Noto.App.Tests;

// A real app stack over in-memory SQLite and a fake clock (Wed 2026-10-07 10:00 UTC, workspace zone UTC).
public sealed class AppFixture : IDisposable
{
    public static readonly DateOnly Today = new(2026, 10, 7);

    public SqliteUnitOfWork Db { get; } = SqliteUnitOfWork.InMemory();
    public FakeClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-07T10:00:00Z"));
    public AppServices Services { get; }
    public ViewModelFactory Vms { get; }
    public Workspace Workspace { get; private set; } = null!;

    public AppFixture(Preset? preset = null)
    {
        var bus = new CommandBus(Db, Clock, Guid.CreateVersion7());
        var platform = new PlatformServices(
            new UnsupportedHotkey("test"),
            new InMemoryKeyring(),
            new UnsupportedCaptureContext("test"),
            new UnsupportedNotifications("test"),
            new StaticReduceMotion()
        );
        Services = new AppServices(Db, bus, Clock, Db, platform);
        Vms = new ViewModelFactory(Services);
        Workspace = Services
            .Workspaces.CreateAsync("Work", "work", preset ?? BuiltInPresets.Sprint, 0)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<Guid> AddAsync(
        string title,
        DateOnly? plannedFor = null,
        int? estimate = null
    )
    {
        var id = Guid.CreateVersion7();
        var commands = new List<ItemCommand>
        {
            new CreateItem(id, Workspace.Id, title, plannedFor),
        };
        if (estimate is { } e)
            commands.Add(new SetEstimate(id, e));
        foreach (var c in commands)
            await Services.Bus.SendAsync(c);
        return id;
    }

    public async Task<TodoItem> GetAsync(Guid id) =>
        (await Db.RunAsync(s => s.Items.GetAsync(id)))!;

    public void NextDay() => Clock.Advance(TimeSpan.FromDays(1));

    public void Dispose() => Db.Dispose();
}
