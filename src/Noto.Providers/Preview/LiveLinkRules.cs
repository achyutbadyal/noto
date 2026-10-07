using Noto.Core.Models;
using Noto.Core.Links;

namespace Noto.Providers.Preview;

public enum SuggestionKind { MarkDone, ReturnFromWaiting }

public sealed record LinkSuggestion(SuggestionKind Kind, Guid ItemId, Guid WorkspaceId, string Url, string Message);

// Rules are written once against the normalized LinkState (docs/10 › Live Links).
public static class LiveLinkRules
{
    public static IReadOnlyList<LinkSuggestion> Evaluate(TodoItem item, LinkChange change)
    {
        if (item.Status is not (ItemStatus.Open or ItemStatus.Waiting) || item.DeletedAt is not null) return [];

        var suggestions = new List<LinkSuggestion>();
        var name = change.Preview.Subtitle ?? change.Preview.Title;
        var kind = change.Preview.Metadata.TryGetValue("kind", out var k) ? k.GetString() : null;
        var finished = IsFinished(change.To) && !IsFinished(change.From);

        if (finished)
            suggestions.Add(new(SuggestionKind.MarkDone, item.Id, item.WorkspaceId, change.Url,
                kind == "pr" ? "Linked PR merged. Done?" : $"{name} closed. Done?"));

        // A waiting item comes back when its link stops being blocked/in review, or finishes.
        var leftBlock = IsBlocking(change.From) && !IsBlocking(change.To);
        if (item.Status == ItemStatus.Waiting && (leftBlock || finished))
            suggestions.Add(new(SuggestionKind.ReturnFromWaiting, item.Id, item.WorkspaceId, change.Url, $"{name} unblocked. Back on today."));

        return suggestions;
    }

    static bool IsFinished(LinkState? s) => s is LinkState.Done or LinkState.Closed;
    static bool IsBlocking(LinkState? s) => s is LinkState.Blocked or LinkState.InReview;
}
