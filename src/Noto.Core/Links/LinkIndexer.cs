using Noto.Core.Interfaces;
using Noto.Core.Time;

namespace Noto.Core.Links;

// Keeps todo_link rows in step with the URLs found in an item's title and notes.
public sealed class LinkIndexer(IUnitOfWork uow, IClock clock)
{
    public Task<IReadOnlyList<TodoLink>> IndexAsync(Guid itemId, string? title, string? notes) =>
        uow.RunAsync(async store =>
        {
            await store.Links.ReplaceTextLinksAsync(
                itemId,
                LinkUrl.Scan(title, notes),
                clock.UtcNow
            );
            return await store.Links.ListForItemAsync(itemId);
        });

    public Task AddExplicitAsync(Guid itemId, string url) =>
        uow.RunAsync(async store =>
        {
            var normalized =
                LinkUrl.Normalize(url)
                ?? throw new ArgumentException("Not an http(s) URL", nameof(url));
            await store.Links.AddExplicitAsync(itemId, normalized, clock.UtcNow);
            return 0;
        });
}
