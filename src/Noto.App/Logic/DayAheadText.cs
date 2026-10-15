using System.Globalization;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Text;

namespace Noto.App.Logic;

// The words the day-ahead planner uses for a day's shape. Kept apart from the view model so the phrasing is
// one tested place: how heavy the day is, what is left, and what each part of it holds.
public static class DayAheadText
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // "Tomorrow", "Saturday" within the coming week, else "Mon, Oct 26".
    public static string Title(DateOnly day, DateOnly today) =>
        (day.DayNumber - today.DayNumber) switch
        {
            1 => "Tomorrow",
            > 1 and < 7 => day.ToString("dddd", Invariant),
            _ => day.ToString("ddd, MMM d", Invariant),
        };

    // How a sentence names the day: "tomorrow", "Saturday", "Mon, Oct 26".
    public static string Name(DateOnly day, DateOnly today) =>
        day.DayNumber - today.DayNumber == 1 ? "tomorrow" : Title(day, today);

    // The page heading: the day's name, then its date, so one line says both where and when.
    // "Tomorrow · Thu, Oct 8", "Saturday · Oct 10", "Mon, Oct 26 · in 10 days".
    public static string HeaderTitle(DateOnly day, DateOnly today)
    {
        var delta = day.DayNumber - today.DayNumber;
        return delta switch
        {
            1 => $"Tomorrow · {day.ToString("ddd, MMM d", Invariant)}",
            > 1 and < 7 =>
                $"{day.ToString("dddd", Invariant)} · {day.ToString("MMM d", Invariant)}",
            < 14 => $"{day.ToString("ddd, MMM d", Invariant)} · in {delta} days",
            _ => $"{day.ToString("ddd, MMM d", Invariant)} · in {(delta + 3) / 7} weeks",
        };
    }

    public static string Amount(int amount, CapacityUnit unit) =>
        unit == CapacityUnit.Items
            ? amount == 1
                ? "1 item"
                : $"{amount} items"
            : Duration.Short(amount);

    // "3 tasks · 2h 30m of 6h"
    public static string Headline(DayAheadPlan plan)
    {
        if (plan.PlannedCount == 0)
            return plan.DueOnly.Count > 0
                ? "Nothing planned, but something is due"
                : "Nothing planned yet";
        var tasks = plan.PlannedCount == 1 ? "1 task" : $"{plan.PlannedCount} tasks";
        return plan.Unit == CapacityUnit.Items
            ? $"{tasks} of {plan.Capacity}"
            : $"{tasks} · {Duration.Short(plan.Committed)} of {Duration.Short(plan.Capacity)}";
    }

    // How the day sits against capacity, in a few honest words.
    public static string Verdict(DayAheadPlan plan)
    {
        if (plan.PlannedCount == 0)
            return $"{Amount(plan.Capacity, plan.Unit)} open";
        if (plan.IsOver)
            return $"{Amount(plan.Committed - plan.Capacity, plan.Unit)} over";
        var share = plan.Capacity == 0 ? 1.0 : (double)plan.Committed / plan.Capacity;
        var feel = share switch
        {
            <= 0.4 => "A light day",
            <= 0.85 => "A comfortable day",
            _ => "A full day",
        };
        return $"{feel} · {Amount(plan.Free, plan.Unit)} free";
    }

    public static string PartName(TimeOfDay? part) => part?.ToString() ?? "Any time";

    public static string ReasonText(DaySuggestion s, DateOnly today) =>
        s.Reason switch
        {
            SuggestionReason.DueSoon when s.Item.DueDate is { } due =>
                $"Due {ItemDetail.DayWord(due, today)}",
            SuggestionReason.Overdue when s.Item.DueDate is { } due =>
                $"Overdue · {ItemDetail.DayWord(due, today)}",
            SuggestionReason.Overdue => "Needs a day",
            SuggestionReason.HighPriority => "High priority",
            _ => "No day yet",
        };
}
