using Noto.Core.Commands;
using Noto.Core.Links;

namespace Noto.App.Tests;

// Links in a task's title and notes are kept in step with the text, through every write and undo.
public sealed class LinkIndexingTests : IDisposable
{
    const string Pr = "https://github.com/acme/app/pull/482";
    const string Ticket = "https://acme.atlassian.net/browse/PROJ-88";

    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    async Task<IReadOnlyList<string>> LinksAsync(Guid id) =>
        (await _app.Db.RunAsync(s => s.Links.ListForItemAsync(id))).Select(l => l.Url).ToList();

    [Fact]
    public async Task A_url_in_the_title_is_indexed_when_the_item_is_created()
    {
        var id = Guid.CreateVersion7();
        await _app.Services.Bus.SendAsync(new CreateItem(id, _app.Workspace.Id, $"Review {Pr}"));

        (await LinksAsync(id)).ShouldContain(LinkUrl.Normalize(Pr));
    }

    [Fact]
    public async Task Editing_notes_adds_and_removes_links()
    {
        var id = await _app.AddAsync("Plan the work");

        await _app.Services.Bus.SendAsync(new SetNotes(id, $"See {Ticket}"));
        (await LinksAsync(id)).ShouldContain(LinkUrl.Normalize(Ticket)!);

        await _app.Services.Bus.SendAsync(new SetNotes(id, "no links any more"));
        (await LinksAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Renaming_an_item_replaces_its_title_links()
    {
        var id = await _app.AddAsync($"Review {Pr}");

        await _app.Services.Bus.SendAsync(new RenameItem(id, $"Review {Ticket}"));

        (await LinksAsync(id)).ShouldBe([LinkUrl.Normalize(Ticket)!]);
    }

    [Fact]
    public async Task Undoing_a_rename_restores_the_links_for_the_old_text()
    {
        var id = await _app.AddAsync($"Review {Pr}");
        var undo = (
            await _app.Services.Bus.SendAsync(new RenameItem(id, "Renamed without a link"))
        ).UndoToken;
        (await LinksAsync(id)).ShouldBeEmpty();

        await _app.Services.Bus.UndoAsync(undo);

        (await LinksAsync(id)).ShouldBe([LinkUrl.Normalize(Pr)!]);
    }

    [Fact]
    public async Task A_link_added_by_hand_survives_later_text_edits()
    {
        var id = await _app.AddAsync("Plain title");
        await _app.Services.Bus.SendAsync(new SetNotes(id, "x"));
        await _app.Services.Links.AddExplicitAsync(id, Pr);

        await _app.Services.Bus.SendAsync(new RenameItem(id, "Another title"));

        (await LinksAsync(id)).ShouldContain(LinkUrl.Normalize(Pr)!);
    }
}
