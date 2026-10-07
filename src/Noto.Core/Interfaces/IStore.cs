using Noto.Core.Derivations;
using Noto.Core.Models;

namespace Noto.Core.Interfaces;

public interface IItemRepository
{
    Task<TodoItem?> GetAsync(Guid id);
    Task UpsertAsync(TodoItem item);
    Task<IReadOnlyList<TodoItem>> ListAsync(Guid workspaceId);
    // Open and waiting items plus those completed on `today`: everything the Today view can show.
    Task<IReadOnlyList<TodoItem>> ListForTodayAsync(Guid workspaceId, DateOnly today);
    // Includes tombstones; sync needs them to push deletes.
    Task<IReadOnlyList<TodoItem>> ListAllAsync(Guid workspaceId);
}

public interface IWorkspaceRepository
{
    Task<Workspace?> GetAsync(Guid id);
    Task UpsertAsync(Workspace workspace);
    Task<IReadOnlyList<Workspace>> ListAsync();
    Task<IReadOnlyList<Workspace>> ListDeletedAsync();
}

public interface IEventStore
{
    Task AppendAsync(ItemEvent itemEvent);
    Task<IReadOnlyList<ItemEvent>> ListForItemAsync(Guid itemId);
    Task<IReadOnlyList<ItemEvent>> ListForItemsAsync(IReadOnlyCollection<Guid> itemIds);
    Task<IReadOnlyList<ItemEvent>> ListForWorkspaceAsync(Guid workspaceId);
}

// Local-only derived caches; always safe to drop and rebuild.
public interface ICacheStore
{
    Task<IReadOnlyDictionary<DateOnly, DayStats>> GetDayStatsAsync(Guid workspaceId, DateOnly from, DateOnly to);
    Task PutDayStatsAsync(Guid workspaceId, DayStats stats);
    Task InvalidateDayStatsFromAsync(Guid workspaceId, DateOnly from);

    Task<IReadOnlyDictionary<Guid, ItemMetrics>> GetMetricsAsync(IReadOnlyCollection<Guid> itemIds, DateOnly asOf);
    Task PutMetricsAsync(Guid itemId, DateOnly asOf, ItemMetrics metrics);
    Task InvalidateMetricsAsync(Guid itemId);
}

public interface IStore
{
    IItemRepository Items { get; }
    IRecurrenceRuleRepository Rules { get; }
    ITagRepository Tags { get; }
    IDayNoteRepository DayNotes { get; }
    IWorkspaceRepository Workspaces { get; }
    IEventStore Events { get; }
    ICacheStore Caches { get; }
    ISyncStore Sync { get; }
    ISyncRowStore SyncRows { get; }
    ILinkRepository Links { get; }
    IPreviewCache Previews { get; }
    IConnectionRepository Connections { get; }
}

// The only entry point to storage: serializes access and wraps `work` in one transaction.
public interface IUnitOfWork
{
    Task<T> RunAsync<T>(Func<IStore, Task<T>> work);
}
