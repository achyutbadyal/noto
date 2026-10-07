using System.Text.Json.Nodes;
using Noto.Core.Models;

namespace Noto.Core.Presets;

public enum PressureState
{
    Fresh,
    Warm,
    Hot,
    Stale,
}

// Single source of escalation thresholds (docs/03 › Pressure). All apply to carry, not age.
public sealed record PressureThresholds(
    int Warm,
    int Hot,
    int Stale,
    int StuckCarry,
    int StuckDefers
)
{
    public static PressureThresholds For(Pressure level, string? overridesJson = null)
    {
        var baseline = level switch
        {
            Pressure.Gentle => new PressureThresholds(5, 10, 14, 14, 5),
            Pressure.Honest => new PressureThresholds(1, 3, 6, 3, 3),
            _ => new PressureThresholds(1, 2, 4, 2, 2),
        };
        return overridesJson is null
            ? baseline
            : baseline.WithOverrides(JsonNode.Parse(overridesJson)!.AsObject());
    }

    PressureThresholds WithOverrides(JsonObject o) =>
        new(
            o["warm"]?.GetValue<int>() ?? Warm,
            o["hot"]?.GetValue<int>() ?? Hot,
            o["stale"]?.GetValue<int>() ?? Stale,
            o["stuck_carry"]?.GetValue<int>() ?? StuckCarry,
            o["stuck_defers"]?.GetValue<int>() ?? StuckDefers
        );

    public PressureState StateOf(int carry) =>
        carry >= Stale ? PressureState.Stale
        : carry >= Hot ? PressureState.Hot
        : carry >= Warm ? PressureState.Warm
        : PressureState.Fresh;

    public bool IsStuck(int carry, int defers) => carry >= StuckCarry || defers >= StuckDefers;
}
