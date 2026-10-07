using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Tests;
using Noto.Core.Workspaces;

namespace Noto.Data.Tests;

// Fixed Wednesday 2026-10-07 10:00 UTC, UTC workspace, one Sprint workspace.
public sealed class FeatureFixture : IDisposable
{
    public SqliteUnitOfWork Uow { get; } = SqliteUnitOfWork.InMemory();
    public FakeClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-07T10:00:00Z"));
    public CommandBus Bus { get; }
    public DerivationService Derive { get; }
    public WorkspaceService Workspaces { get; }
    public Workspace Ws { get; }

    public FeatureFixture()
    {
        Bus = new CommandBus(Uow, Clock, Guid.CreateVersion7());
        Derive = new DerivationService(Uow, Clock);
        Workspaces = new WorkspaceService(Uow, Clock);
        Ws = Workspaces
            .CreateAsync("Work", "work", "#fff", BuiltInPresets.Sprint)
            .GetAwaiter()
            .GetResult();
        Ws.TzFollowsDevice = false;
        Ws.TimeZone = "UTC";
        Uow.RunAsync(async s =>
            {
                await s.Workspaces.UpsertAsync(Ws);
                return 0;
            })
            .GetAwaiter()
            .GetResult();
    }

    public async Task<Guid> CreateItemAsync(string title, DateOnly? planned = null)
    {
        var id = Guid.CreateVersion7();
        await Bus.SendAsync(new CreateItem(id, Ws.Id, title, planned));
        return id;
    }

    public Task<TodoItem> LoadAsync(Guid id) =>
        Uow.RunAsync(async s => (await s.Items.GetAsync(id))!);

    public void Dispose() => Uow.Dispose();
}
