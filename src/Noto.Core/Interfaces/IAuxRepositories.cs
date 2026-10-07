using Noto.Core.Models;

namespace Noto.Core.Interfaces;

public interface IRecurrenceRuleRepository
{
    Task<RecurrenceRule?> GetAsync(Guid id);
    Task UpsertAsync(RecurrenceRule rule);
    Task<IReadOnlyList<RecurrenceRule>> ListAsync(Guid workspaceId);
}

public interface ITagRepository
{
    Task UpsertAsync(Tag tag);
    Task<IReadOnlyList<Tag>> ListAsync(Guid workspaceId);
    Task SetItemTagsAsync(Guid itemId, IReadOnlyCollection<Guid> tagIds);
    Task<IReadOnlyList<Guid>> GetItemTagIdsAsync(Guid itemId);
    Task<IReadOnlyList<(Guid ItemId, Guid TagId)>> ListItemTagsAsync(Guid workspaceId);
}

public interface IDayNoteRepository
{
    Task<DayNote?> GetAsync(Guid workspaceId, DateOnly day, DayNoteKind kind);
    Task UpsertAsync(DayNote note);
    Task<IReadOnlyList<DayNote>> ListAsync(Guid workspaceId);
}
