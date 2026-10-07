namespace Noto.App.Services;

// The shutdown "remember for tomorrow" line, shown at the top of the next morning review.
public interface IDayNotes
{
    Task<string?> GetAsync(Guid workspaceId, DateOnly day);
    Task SetAsync(Guid workspaceId, DateOnly day, string text);
}

// Day notes stored (and synced) like any other user data.
public sealed class StoredDayNotes(Noto.Core.Workspaces.DayNoteService service) : IDayNotes
{
    public Task<string?> GetAsync(Guid workspaceId, DateOnly day) => service.GetNoteAsync(workspaceId, day);
    public Task SetAsync(Guid workspaceId, DateOnly day, string text) => service.SetNoteAsync(workspaceId, day, text);
}

public sealed class InMemoryDayNotes : IDayNotes
{
    readonly Dictionary<(Guid, DateOnly), string> _notes = [];

    public Task<string?> GetAsync(Guid workspaceId, DateOnly day) =>
        Task.FromResult(_notes.TryGetValue((workspaceId, day), out var text) ? text : null);

    public Task SetAsync(Guid workspaceId, DateOnly day, string text)
    {
        _notes[(workspaceId, day)] = text;
        return Task.CompletedTask;
    }
}
