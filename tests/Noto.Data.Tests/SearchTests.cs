using Noto.Core.Commands;
using Noto.Core.Models;
using Noto.Core.Tests;

namespace Noto.Data.Tests;

public sealed class SearchTests : IDisposable
{
    readonly SqliteUnitOfWork _uow = SqliteUnitOfWork.InMemory();
    readonly CommandBus _bus;
    readonly Workspace _ws = Make.Workspace();
    readonly Workspace _other = Make.Workspace();

    public SearchTests()
    {
        _bus = new CommandBus(
            _uow,
            new FakeClock(DateTimeOffset.Parse("2026-10-07T10:00:00Z")),
            Guid.CreateVersion7()
        );
        _uow.RunAsync(async s =>
            {
                await s.Workspaces.UpsertAsync(_ws);
                await s.Workspaces.UpsertAsync(_other);
                return 0;
            })
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose() => _uow.Dispose();

    async Task<Guid> Add(string title, Workspace? ws = null)
    {
        var id = Guid.CreateVersion7();
        await _bus.SendAsync(new CreateItem(id, (ws ?? _ws).Id, title, new DateOnly(2026, 10, 7)));
        return id;
    }

    [Fact]
    public async Task Finds_by_prefix_and_ignores_fts_syntax_in_input()
    {
        var id = await Add("Deploy v2.3 to staging");
        await Add("Write API tests");

        (await _uow.SearchAsync("depl")).ShouldHaveSingleItem().ItemId.ShouldBe(id);
        (await _uow.SearchAsync("\"deploy\" ( *")).ShouldHaveSingleItem().ItemId.ShouldBe(id);
        (await _uow.SearchAsync("   ")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Index_follows_renames_notes_and_deletes()
    {
        var id = await Add("Old title");
        await _bus.SendAsync(new RenameItem(id, "Brand new"));
        (await _uow.SearchAsync("old")).ShouldBeEmpty();
        (await _uow.SearchAsync("brand")).Count.ShouldBe(1);

        await _bus.SendAsync(new SetNotes(id, "remember the milk"));
        (await _uow.SearchAsync("milk")).Count.ShouldBe(1);

        await _bus.SendAsync(new DeleteItem(id));
        (await _uow.SearchAsync("brand")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Can_scope_to_a_workspace()
    {
        await Add("shared word", _ws);
        await Add("shared word", _other);

        (await _uow.SearchAsync("shared")).Count.ShouldBe(2);
        (await _uow.SearchAsync("shared", _other.Id))
            .ShouldHaveSingleItem()
            .WorkspaceId.ShouldBe(_other.Id);
    }
}
