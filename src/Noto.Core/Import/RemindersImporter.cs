using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace Noto.Core.Import;

// Apple Reminders exports as iCalendar: one VTODO per reminder.
public static class RemindersImporter
{
    // EventKit/RFC 5545 priority: 1–4 high, 5 medium, 6–9 low, 0 none.
    static int Priority(int p) =>
        p switch
        {
            0 => 0,
            <= 4 => 3,
            5 => 2,
            _ => 1,
        };

    public static IReadOnlyList<ImportedItem> Parse(string content)
    {
        Calendar calendar;
        try
        {
            calendar =
                Calendar.Load(content) ?? throw new ImportFormatException("Not an iCalendar file");
        }
        catch (Exception e) when (e is not ImportFormatException)
        {
            throw new ImportFormatException("Not an iCalendar file");
        }

        return calendar.Todos.Select(ToItem).OfType<ImportedItem>().ToList();
    }

    static ImportedItem? ToItem(Todo t)
    {
        var title = t.Summary?.Trim();
        if (string.IsNullOrEmpty(title))
            return null;
        var done = string.Equals(t.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
        return new ImportedItem(
            title,
            ImportHelpers.NullIfBlank(t.Description),
            Date: Day(t.Due),
            Priority: Priority(t.Priority),
            IsDone: done,
            CompletedOn: done ? Day(t.Completed) : null,
            Tags: t.Categories?.ToList() ?? []
        );
    }

    static DateOnly? Day(CalDateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);
}
