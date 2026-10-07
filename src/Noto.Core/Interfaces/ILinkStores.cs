using Noto.Core.Links;

namespace Noto.Core.Interfaces;

// Synced (todo_link): the URL is user data even though previews are not.
public interface ILinkRepository
{
    Task<IReadOnlyList<TodoLink>> ListForItemAsync(Guid itemId);
    Task<IReadOnlyList<TodoLink>> ListForItemsAsync(IReadOnlyCollection<Guid> itemIds);
    // Items currently linking to a normalized URL (live-link reactions).
    Task<IReadOnlyList<Guid>> ListItemIdsForUrlAsync(string url);
    // Replaces links detected in text; explicit links are left alone.
    Task ReplaceTextLinksAsync(Guid itemId, IReadOnlyList<string> urls, DateTimeOffset now);
    Task AddExplicitAsync(Guid itemId, string url, DateTimeOffset now);
    Task RemoveAsync(Guid linkId, DateTimeOffset now);
}

// Local-only, keyed by normalized URL so one PR in three todos is fetched once.
public interface IPreviewCache
{
    Task<LinkPreview?> GetAsync(string url);
    Task<IReadOnlyDictionary<string, LinkPreview>> GetManyAsync(IReadOnlyCollection<string> urls);
    // Upsert that preserves the viewed-state columns.
    Task PutAsync(LinkPreview preview);
    Task MarkViewedAsync(string url, DateTimeOffset now);
    Task DeleteForConnectionAsync(Guid connectionId);
}

// Local-only connection metadata; secrets are in IKeyring.
public interface IConnectionRepository
{
    Task<AppConnection?> GetAsync(Guid id);
    Task<IReadOnlyList<AppConnection>> ListAsync();
    Task UpsertAsync(AppConnection connection);
    Task DeleteAsync(Guid id);
}

public interface IKeyring
{
    Task<string?> GetAsync(string service, string account);
    Task SetAsync(string service, string account, string secret);
    Task DeleteAsync(string service, string account);
}
