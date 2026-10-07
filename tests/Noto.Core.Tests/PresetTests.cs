using Noto.Core.Derivations;
using Noto.Core.Models;
using Noto.Core.Presets;

namespace Noto.Core.Tests;

public class PresetTests
{
    [Theory]
    [InlineData(Pressure.Gentle, 4, PressureState.Fresh)]
    [InlineData(Pressure.Gentle, 5, PressureState.Warm)]
    [InlineData(Pressure.Gentle, 10, PressureState.Hot)]
    [InlineData(Pressure.Gentle, 14, PressureState.Stale)]
    [InlineData(Pressure.Honest, 0, PressureState.Fresh)]
    [InlineData(Pressure.Honest, 2, PressureState.Warm)]
    [InlineData(Pressure.Honest, 3, PressureState.Hot)]
    [InlineData(Pressure.Honest, 5, PressureState.Hot)]
    [InlineData(Pressure.Honest, 6, PressureState.Stale)]
    [InlineData(Pressure.Relentless, 1, PressureState.Warm)]
    [InlineData(Pressure.Relentless, 3, PressureState.Hot)]
    [InlineData(Pressure.Relentless, 4, PressureState.Stale)]
    public void Pressure_table_matches_the_spec(Pressure level, int carry, PressureState expected) =>
        PressureThresholds.For(level).StateOf(carry).ShouldBe(expected);

    [Theory]
    [InlineData(Pressure.Gentle, 13, 4, false)]
    [InlineData(Pressure.Gentle, 14, 0, true)]
    [InlineData(Pressure.Gentle, 0, 5, true)]
    [InlineData(Pressure.Honest, 2, 2, false)]
    [InlineData(Pressure.Honest, 3, 0, true)]
    [InlineData(Pressure.Relentless, 2, 0, true)]
    public void Stuck_chip_thresholds(Pressure level, int carry, int defers, bool stuck) =>
        PressureThresholds.For(level).IsStuck(carry, defers).ShouldBe(stuck);

    [Fact]
    public void Overrides_replace_individual_numbers()
    {
        var t = PressureThresholds.For(Pressure.Honest, """{"hot": 8}""");
        t.Hot.ShouldBe(8);
        t.Warm.ShouldBe(1);
    }

    [Fact]
    public void Applying_a_preset_sets_the_three_controls_and_editing_marks_it_custom()
    {
        var ws = Make.Workspace();
        BuiltInPresets.Kanban.ApplyTo(ws);
        (ws.Layout, ws.SortOrderMode, ws.Pressure).ShouldBe((Layout.Board, SortOrderMode.Manual, Pressure.Gentle));
        BuiltInPresets.Label(ws).ShouldBe("kanban");

        ws.Pressure = Pressure.Relentless;
        BuiltInPresets.Label(ws).ShouldBe("kanban (custom)");
    }

    [Fact]
    public void Six_built_in_presets()
    {
        BuiltInPresets.All.Select(p => p.Id).ShouldBe(["sprint", "zen", "deadline", "habit", "kanban", "accountability"]);
    }
}

public class OrderingTests
{
    static readonly DateOnly Today = new(2026, 10, 7);
    static readonly Func<TodoItem, ItemMetrics> NoMetrics = _ => new(0, 0, 0);

    static TodoItem Item(string title, int priority = 0, DateOnly? due = null, TimeOfDay? tod = null, string rank = "a")
    {
        var i = Make.Item();
        i.Title = title; i.Priority = priority; i.DueDate = due; i.TimeOfDay = tod; i.ManualRank = rank;
        return i;
    }

    static string[] Titles(IEnumerable<TodoItem> items) => items.Select(i => i.Title).ToArray();

    [Fact]
    public void Manual_uses_rank()
    {
        var sorted = ItemOrdering.Sort([Item("b", rank: "b"), Item("a", rank: "a")], SortOrderMode.Manual, NoMetrics, Today);
        Titles(sorted).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Priority_then_carry_then_rank()
    {
        var hi = Item("hi", priority: 3);
        var lowCarried = Item("lowCarried", priority: 1);
        var low = Item("low", priority: 1, rank: "b");
        Func<TodoItem, ItemMetrics> m = i => new(0, i == lowCarried ? 5 : 0, 0);

        Titles(ItemOrdering.Sort([low, lowCarried, hi], SortOrderMode.PriorityCarry, m, Today))
            .ShouldBe(["hi", "lowCarried", "low"]);
    }

    [Fact]
    public void Due_date_puts_overdue_first_and_undated_last()
    {
        var items = new[]
        {
            Item("none"), Item("later", due: Today.AddDays(3)), Item("overdue", due: Today.AddDays(-2)),
            Item("soon", due: Today.AddDays(1)),
        };
        Titles(ItemOrdering.Sort(items, SortOrderMode.DueDate, NoMetrics, Today)).ShouldBe(["overdue", "soon", "later", "none"]);
    }

    [Fact]
    public void Carry_desc_breaks_ties_by_age()
    {
        var a = Item("a"); var b = Item("b"); var c = Item("c");
        var metrics = new Dictionary<Guid, ItemMetrics>
        {
            [a.Id] = new(10, 2, 0), [b.Id] = new(3, 5, 0), [c.Id] = new(20, 2, 0),
        };
        Titles(ItemOrdering.Sort([a, b, c], SortOrderMode.CarryDesc, i => metrics[i.Id], Today)).ShouldBe(["b", "c", "a"]);
    }

    [Fact]
    public void Time_of_day_orders_morning_to_evening_then_unset()
    {
        var items = new[] { Item("unset"), Item("eve", tod: TimeOfDay.Evening), Item("morn", tod: TimeOfDay.Morning) };
        Titles(ItemOrdering.Sort(items, SortOrderMode.TimeOfDay, NoMetrics, Today)).ShouldBe(["morn", "eve", "unset"]);
    }
}
