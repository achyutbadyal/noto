using Noto.Core.Import;
using static Noto.Core.Tests.Story;

namespace Noto.Core.Tests;

public class CsvTests
{
    [Fact]
    public void Parses_quotes_escapes_and_embedded_newlines()
    {
        var rows = Csv.Parse("a,\"b,c\",\"say \"\"hi\"\"\"\r\n\"line1\nline2\",,x\n");
        rows.Count.ShouldBe(2);
        rows[0].ShouldBe(["a", "b,c", "say \"hi\""]);
        rows[1].ShouldBe(["line1\nline2", "", "x"]);
    }

    [Fact]
    public void Unterminated_quote_is_an_error() =>
        Should.Throw<ImportFormatException>(() => Csv.Parse("a,\"b"));

    [Fact]
    public void Writer_quotes_and_guards_against_spreadsheet_formulas()
    {
        Csv.Row(["a,b"]).ShouldBe("\"a,b\"");
        Csv.Row(["=SUM(A1)"]).ShouldBe("'=SUM(A1)");
        Csv.Row(["-5"]).ShouldBe("'-5");
        Csv.Row(["plain"]).ShouldBe("plain");
        Csv.Row(["=x"], guardFormulas: false).ShouldBe("=x");
    }

    [Fact]
    public void Writer_output_parses_back()
    {
        var row = Csv.Row(["a,b", "q\"uote", "multi\nline"]);
        Csv.Parse(row)[0].ShouldBe(["a,b", "q\"uote", "multi\nline"]);
    }
}

public class TodoistImporterTests
{
    const string Csv = """
        TYPE,CONTENT,DESCRIPTION,PRIORITY,INDENT,AUTHOR,RESPONSIBLE,DATE,DATE_LANG,TIMEZONE
        section,Work,,,,,,,,
        task,Write report,"Q3, final",1,1,Me (1),,2026-10-10,en,UTC
        task,Gather numbers,,4,2,Me (1),,,en,
        task,Send to team,,3,2,Me (1),,,en,
        task,Water plants,,4,1,Me (1),,every Monday,en,
        note,a note,,,,,,,,
        """;

    [Fact]
    public void Parses_tasks_priority_dates_and_nesting()
    {
        var items = TodoistImporter.ParseCsv(Csv);

        items.Select(i => i.Title).ShouldBe(["Write report", "Water plants"]);
        var report = items[0];
        (report.Notes, report.Priority, report.Date).ShouldBe(
            ("Q3, final", 4, new DateOnly(2026, 10, 10))
        );
        report
            .Subtasks!.Select(s => (s.Title, s.Priority))
            .ShouldBe([("Gather numbers", 0), ("Send to team", 2)]);
        items[1].Date.ShouldBeNull(); // "every Monday" isn't a date
    }

    [Fact]
    public void Rejects_other_csv() =>
        Should.Throw<ImportFormatException>(() => TodoistImporter.ParseCsv("a,b\n1,2"));

    const string Json = """
        {"items":[
          {"id":"1","content":"Parent","description":"d","priority":4,"due":{"date":"2026-10-12T09:00:00"},"labels":["work"],"checked":false},
          {"id":"2","parent_id":"1","content":"Child","priority":1,"checked":1,"completed_at":"2026-10-01T10:00:00Z"},
          {"id":"3","content":"Solo","priority":2,"checked":true}
        ]}
        """;

    [Fact]
    public void Parses_api_json_with_parent_links_and_completion()
    {
        var items = TodoistImporter.ParseJson(Json);

        items.Count.ShouldBe(2);
        var parent = items[0];
        (parent.Priority, parent.Date).ShouldBe((4, new DateOnly(2026, 10, 12)));
        parent.Tags.ShouldBe(["work"]);
        var child = parent.Subtasks.ShouldHaveSingleItem();
        (child.IsDone, child.CompletedOn).ShouldBe((true, new DateOnly(2026, 10, 1)));
        items[1].IsDone.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_a_bare_array() =>
        TodoistImporter
            .ParseJson("""[{"id":"1","content":"X","priority":1}]""")
            .ShouldHaveSingleItem()
            .Title.ShouldBe("X");
}

public class ThingsImporterTests
{
    const string Json = """
        [
          {"title":"Buy milk","notes":"2%","status":"open","start":"Anytime","start_date":"2026-10-08","deadline":"2026-10-09","tags":["errand"]},
          {"title":"Learn Rust","status":"open","start":"Someday"},
          {"title":"Done thing","status":"completed","stop_date":"2026-10-02"},
          {"title":"Cancelled","status":"canceled"},
          {"title":"Trip","status":"open","checklist":[{"title":"Passport","status":"completed"},{"title":"Tickets","status":"open"}]}
        ]
        """;

    [Fact]
    public void Maps_status_start_and_dates()
    {
        var items = ThingsImporter.Parse(Json);

        items.Select(i => i.Title).ShouldBe(["Buy milk", "Learn Rust", "Done thing", "Trip"]);
        (items[0].Date, items[0].Due, items[0].Notes).ShouldBe(
            (new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), "2%")
        );
        items[1].IsSomeday.ShouldBeTrue();
        (items[2].IsDone, items[2].CompletedOn).ShouldBe((true, new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public void Checklists_become_subtasks()
    {
        var trip = ThingsImporter.Parse(Json).Last();
        trip.Subtasks!.Select(s => (s.Title, s.IsDone))
            .ShouldBe([("Passport", true), ("Tickets", false)]);
    }
}

public class RemindersImporterTests
{
    const string Ics =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n"
        + "BEGIN:VTODO\r\nSUMMARY:Call mum\r\nDESCRIPTION:About the\\, trip\\nbring photos\r\nDUE;VALUE=DATE:20261010\r\nPRIORITY:1\r\nSTATUS:NEEDS-ACTION\r\nEND:VTODO\r\n"
        + "BEGIN:VTODO\r\nSUMMARY:Pay a very long\r\n  title that folds\r\nDUE:20261011T090000Z\r\nPRIORITY:5\r\nSTATUS:COMPLETED\r\nCOMPLETED:20261009T120000Z\r\nCATEGORIES:home,money\r\nEND:VTODO\r\n"
        + "BEGIN:VTODO\r\nSUMMARY:Low\r\nPRIORITY:9\r\nEND:VTODO\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void Parses_vtodos_with_unfolding_unescaping_and_priority_mapping()
    {
        var items = RemindersImporter.Parse(Ics);

        items.Count.ShouldBe(3);
        (items[0].Title, items[0].Notes, items[0].Date, items[0].Priority).ShouldBe(
            ("Call mum", "About the, trip\nbring photos", new DateOnly(2026, 10, 10), 3)
        );
        items[1].Title.ShouldBe("Pay a very long title that folds");
        (items[1].IsDone, items[1].CompletedOn, items[1].Priority).ShouldBe(
            (true, new DateOnly(2026, 10, 9), 2)
        );
        items[1].Tags.ShouldBe(["home", "money"]);
        (items[2].Priority, items[2].Date).ShouldBe((1, null));
    }

    [Fact]
    public void Rejects_non_ics() =>
        Should.Throw<ImportFormatException>(() => RemindersImporter.Parse("hello"));
}

public class TickTickImporterTests
{
    const string Csv =
        "\"Date: 2026-10-07+0000\"\n\"Version: 7.1\"\n\"Status: 0 Normal; 1 Completed; 2 Archived\"\n"
        + "\"Folder Name\",\"List Name\",\"Title\",\"Kind\",\"Tags\",\"Content\",\"Is Check list\",\"Start Date\",\"Due Date\",\"Reminder\",\"Repeat\",\"Priority\",\"Status\",\"Created Time\",\"Completed Time\",\"Order\",\"Timezone\",\"Is All Day\",\"Is Floating\",\"Column Name\",\"Column Order\",\"View Mode\",\"taskId\",\"parentId\"\n"
        + "\"\",\"Inbox\",\"Plan trip\",\"TEXT\",\"travel, fun\",\"notes\",\"N\",\"2026-10-09T00:00:00+0000\",\"2026-10-12T00:00:00+0000\",\"\",\"\",\"5\",\"0\",\"2026-10-01T10:00:00+0000\",\"\",\"1\",\"UTC\",\"true\",\"false\",\"\",\"\",\"list\",\"1\",\"\"\n"
        + "\"\",\"Inbox\",\"Book flights\",\"TEXT\",\"\",\"\",\"N\",\"\",\"\",\"\",\"\",\"0\",\"1\",\"2026-10-01T10:00:00+0000\",\"2026-10-05T08:00:00+0000\",\"2\",\"UTC\",\"true\",\"false\",\"\",\"\",\"list\",\"2\",\"1\"\n"
        + "\"\",\"Inbox\",\"Old\",\"TEXT\",\"\",\"\",\"N\",\"\",\"\",\"\",\"\",\"0\",\"2\",\"\",\"\",\"3\",\"UTC\",\"true\",\"false\",\"\",\"\",\"list\",\"3\",\"\"\n";

    [Fact]
    public void Skips_metadata_lines_and_archived_tasks_and_builds_the_tree()
    {
        var items = TickTickImporter.Parse(Csv);

        var trip = items.ShouldHaveSingleItem();
        (trip.Title, trip.Priority, trip.Date, trip.Due).ShouldBe(
            ("Plan trip", 3, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12))
        );
        trip.Tags.ShouldBe(["travel", "fun"]);
        var flights = trip.Subtasks.ShouldHaveSingleItem();
        (flights.IsDone, flights.CompletedOn).ShouldBe((true, new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public void Rejects_other_csv() =>
        Should.Throw<ImportFormatException>(() => TickTickImporter.Parse("a,b\n1,2"));
}

public class MarkdownImporterTests
{
    const string Md = """
        # Groceries
        - [ ] Milk #home due:2026-10-09
        - [x] Bread
          - [ ] Sub item
          - [x] Other sub

        plain text
        - plain bullet
        * [ ] Eggs
        """;

    [Fact]
    public void Parses_checkboxes_tokens_and_nesting()
    {
        var items = MarkdownImporter.Parse(Md);

        items.Select(i => i.Title).ShouldBe(["Milk", "Bread", "Eggs"]);
        items[0].Due.ShouldBe(new DateOnly(2026, 10, 9));
        items[0].Tags.ShouldBe(["home"]);
        items[1].IsDone.ShouldBeTrue();
        items[1]
            .Subtasks!.Select(s => (s.Title, s.IsDone))
            .ShouldBe([("Sub item", false), ("Other sub", true)]);
    }

    [Fact]
    public void Tabs_count_as_nesting()
    {
        var items = MarkdownImporter.Parse("- [ ] A\n\t- [ ] B\n");
        items.ShouldHaveSingleItem().Subtasks.ShouldHaveSingleItem().Title.ShouldBe("B");
    }

    [Fact]
    public void Dispatches_by_format()
    {
        Importers.Parse(ImportFormat.MarkdownChecklist, "- [ ] x").ShouldHaveSingleItem();
        Importers
            .Parse(ImportFormat.TodoistJson, """[{"id":"1","content":"y"}]""")
            .ShouldHaveSingleItem();
    }
}
