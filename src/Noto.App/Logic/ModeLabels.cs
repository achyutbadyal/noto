using Noto.Core.Models;

namespace Noto.App.Logic;

// Human words for the mode enums. One place, so the settings dropdowns, the command bar and the
// in-app guide all describe a mode the same way — "PriorityCarry" tells a new user nothing.
public static class ModeLabels
{
    public static string Of(Layout value) =>
        value switch
        {
            Layout.List => "List",
            Layout.Board => "Board",
            Layout.Timeline => "Timeline",
            Layout.HabitGrid => "Habit grid",
            _ => value.ToString(),
        };

    public static string Of(SortOrderMode value) =>
        value switch
        {
            SortOrderMode.Manual => "Manual",
            SortOrderMode.PriorityCarry => "Priority + carry",
            SortOrderMode.DueDate => "Due date",
            SortOrderMode.CarryDesc => "Carry",
            SortOrderMode.TimeOfDay => "Time of day",
            _ => value.ToString(),
        };

    public static string Of(Pressure value) =>
        value switch
        {
            Pressure.Gentle => "Gentle",
            Pressure.Honest => "Honest",
            Pressure.Relentless => "Relentless",
            _ => value.ToString(),
        };

    public static string Of(CapacityUnit value) =>
        value switch
        {
            CapacityUnit.Minutes => "Minutes",
            CapacityUnit.Items => "Items",
            _ => value.ToString(),
        };
}
