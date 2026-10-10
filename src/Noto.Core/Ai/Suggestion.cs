using Noto.Core.Links;
using Noto.Core.Models;

namespace Noto.Core.Ai;

// What an AI pass proposes for the "New task" form. It is deliberately a *draft*: it is shown in the
// form and never written to an item. Only the form's own submit path creates tasks, so a wrong guess
// can never reach the op-log (docs/07 §19 — AI suggests, the user decides).
public sealed record FieldSuggestion
{
    public string? Title { get; init; }
    public int? EstimateMinutes { get; init; }
    public int? Priority { get; init; } // 0 none … 4 lowest
    public DateOnly? PlannedFor { get; init; }
    public DateOnly? DueDate { get; init; }
    public TimeOfDay? TimeOfDay { get; init; }
    public string? WaitingOn { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    // 0..1. Shown as evidence next to the fill, never used to gate it.
    public double Confidence { get; init; }

    // One short sentence: why these fields. Makes the fill explainable instead of magic.
    public string? Rationale { get; init; }

    public bool IsEmpty =>
        Title is null
        && EstimateMinutes is null
        && Priority is null
        && PlannedFor is null
        && DueDate is null
        && TimeOfDay is null
        && WaitingOn is null
        && Notes is null
        && Tags.Count == 0;
}

// The raw text the user typed, plus the logical day relative dates resolve against. `Today` is passed
// in rather than read from a clock so the suggestion is testable and matches the form's own day.
//
// `Link` is a URL from the text that was already resolved (unauthenticated, or through a connected app)
// so the model is handed what the link *is* rather than left to guess from a bare URL — or worse, to
// invent a title for a page it cannot see.
public sealed record SuggestionRequest(string Text, DateOnly Today, LinkContext? Link = null);

// The seam the UI depends on. Implementations may be a local server, an on-device model or a hosted
// API — the form neither knows nor cares. `IsEnabled` is false when the feature is switched off or has
// no usable provider, and the UI then hides every AI affordance.
public interface ISuggestionService
{
    bool IsEnabled { get; }

    Task<FieldSuggestion?> SuggestAsync(SuggestionRequest request, CancellationToken ct = default);
}

// A suggestion attempt failed for a reason worth showing the user (no provider, bad key, unreachable
// endpoint, a reply that could not be parsed). The form shows `Message` next to the sparkle and leaves
// the fields alone — a failed fill must never look like an empty one.
public sealed class AiSuggestionException(string message, Exception? inner = null)
    : Exception(message, inner);

// The default. Everything degrades to "no AI": the form behaves exactly as it did before.
public sealed class NullSuggestionService : ISuggestionService
{
    public static readonly NullSuggestionService Instance = new();

    public bool IsEnabled => false;

    public Task<FieldSuggestion?> SuggestAsync(
        SuggestionRequest request,
        CancellationToken ct = default
    ) => Task.FromResult<FieldSuggestion?>(null);
}
