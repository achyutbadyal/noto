namespace Noto.Core.Interfaces;

public sealed record SearchHit(Guid ItemId, Guid WorkspaceId, string Title, string? Snippet);

public interface ISearchIndex
{
    // Prefix full-text search over title and notes; `workspaceId` null searches everywhere.
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        Guid? workspaceId = null,
        int limit = 20
    );
}
