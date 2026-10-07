using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.App.Logic;

public sealed record ReviewSuggestion(AppAction Action, string Key, string Text);

// Evidence-based nudges shown as one highlighted key. Never acts on its own (docs/07 §4.1).
public static class ReviewSuggestions
{
    public static ReviewSuggestion? For(
        TodoItem item,
        ItemMetrics metrics,
        PressureThresholds thresholds
    )
    {
        if (metrics.Carry >= 3 && item.EstimateMinutes is > 120)
            return new(
                AppAction.BreakDown,
                "B",
                $"Carried {metrics.Carry} times and estimated over 2h. Break it down?"
            );
        if (metrics.Defers >= thresholds.StuckDefers)
            return new(
                AppAction.Someday,
                "S",
                $"Deferred {metrics.Defers} times. Still worth doing now, or Someday?"
            );
        if (thresholds.IsStuck(metrics.Carry, metrics.Defers))
            return new(AppAction.Drop, "X", $"Carried {metrics.Carry} times. Is it still needed?");
        return null;
    }
}
