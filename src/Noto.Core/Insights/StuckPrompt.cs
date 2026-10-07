using Noto.Core.Commands;
using Noto.Core.Derivations;
using Noto.Core.Presets;

namespace Noto.Core.Insights;

public enum StuckFixKind
{
    BreakDown,
    MoveToWaiting,
    RewriteNextAction,
    MakeNowOrScheduleTomorrow,
    Drop,
}

// The concrete fix offered for a stuck reason (docs/07 §5).
public sealed record StuckFix(StuckFixKind Kind, string Prompt);

public static class StuckPrompt
{
    // The `stuck?` chip and reason prompt appear once carry or defers reach the workspace thresholds.
    public static bool ShouldPrompt(ItemMetrics metrics, PressureThresholds thresholds) =>
        thresholds.IsStuck(metrics.Carry, metrics.Defers);

    public static StuckFix FixFor(StuckReason reason) =>
        reason switch
        {
            StuckReason.TooBig => new(StuckFixKind.BreakDown, "Break it into 2–5 smaller steps"),
            StuckReason.Blocked => new(StuckFixKind.MoveToWaiting, "Who or what is it waiting on?"),
            StuckReason.Unclear => new(
                StuckFixKind.RewriteNextAction,
                "What's the very next physical action?"
            ),
            StuckReason.DontWant => new(
                StuckFixKind.MakeNowOrScheduleTomorrow,
                "Do 25 minutes now, or schedule it first thing tomorrow"
            ),
            _ => new(StuckFixKind.Drop, "Drop it"),
        };
}
