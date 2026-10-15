using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.App.Views;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Models;

namespace Noto.App.Tests.Ui;

// A future day is a plan, not a list of titles: grouped by part of the day, sized against capacity, with
// the notes, facts and links of each task on screen and a hover preview for the rest.
// Set NOTO_SCREENSHOTS=<dir> to keep the PNGs for a visual check.
public sealed class DayAheadTests : IDisposable
{
    readonly AppFixture _app = new();
    static readonly DateOnly Tomorrow = AppFixture.Today.AddDays(1);

    public void Dispose() => _app.Dispose();

    static string? OutDir => Environment.GetEnvironmentVariable("NOTO_SCREENSHOTS");

    async Task<Guid> AddAsync(
        string title,
        DateOnly? day,
        int? minutes = null,
        TimeOfDay? part = null,
        string? notes = null,
        DateOnly? due = null,
        int priority = 0
    )
    {
        var id = await _app.AddAsync(title, day, minutes);
        if (part is not null)
            await _app.Services.Bus.SendAsync(new SetTimeOfDay(id, part));
        if (notes is not null)
            await _app.Services.Bus.SendAsync(new SetNotes(id, notes));
        if (due is not null)
            await _app.Services.Bus.SendAsync(new SetDueDate(id, due));
        if (priority > 0)
            await _app.Services.Bus.SendAsync(new SetPriority(id, priority));
        return id;
    }

    async Task SeedAsync()
    {
        await AddAsync("Today's standup notes", AppFixture.Today, 15);
        await AddAsync(
            "Prepare the Q4 planning deck",
            Tomorrow,
            90,
            TimeOfDay.Morning,
            "Slides live in https://docs.example.com/q4-planning (see the roadmap section).\n- Pull last quarter's numbers\n- Ask Priya for the hiring plan",
            due: Tomorrow,
            priority: 3
        );
        await AddAsync("Dentist appointment", Tomorrow, 60, TimeOfDay.Afternoon, priority: 2);
        await AddAsync("Reply to legal about the contract", Tomorrow, 20);
        var waiting = await AddAsync("API keys from Priya", Tomorrow);
        await _app.Services.Bus.SendAsync(new StartWaiting(waiting, "Priya"));
        await AddAsync("File the expense report", AppFixture.Today.AddDays(3), 30, due: Tomorrow);
        await AddAsync("Book flights to Berlin", null, 20, priority: 4);
        await AddAsync("Renew passport", null, 30, due: AppFixture.Today.AddDays(5));
        await AddAsync("Review PR #482", AppFixture.Today.AddDays(-4), 30);
    }

    (MainWindow Window, ShellViewModel Shell) Open(ThemeVariant variant)
    {
        var shell = new ShellViewModel(_app.Services);
        var window = new MainWindow
        {
            DataContext = shell,
            Width = 1240,
            Height = 860,
        };
        Avalonia.Application.Current!.RequestedThemeVariant = variant;
        window.Show();
        return (window, shell);
    }

    static async Task SettleAsync()
    {
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            // Flyouts and transitions advance on the render clock, which a headless run has to turn by hand.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task SnapAsync(Window window, string name)
    {
        await SettleAsync();
        var frame = window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        if (OutDir is { } dir)
        {
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"));
        }
    }

    [AvaloniaFact]
    public async Task A_future_day_opens_as_a_plan_with_parts_due_dates_and_suggestions()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.ShowDayAsync(Tomorrow);

        var day = shell.Content.ShouldBeOfType<DayAheadViewModel>();
        day.HeaderTitle.ShouldBe("Tomorrow · Thu, Oct 8");
        day.DayName.ShouldBe("tomorrow");
        day.Headline.ShouldBe("3 tasks · 2h 50m of 6h");
        day.Sections.Select(s => s.Title)
            .ShouldBe([
                "Morning",
                "Afternoon",
                "Any time",
                "Waiting on",
                "Due this day",
                "Could go here",
            ]);
        day.Sections.Single(s => s.Title == "Due this day")
            .Rows.Single()
            .Title.ShouldBe("File the expense report");
        day.Sections.Single(s => s.Title == "Could go here")
            .Rows.Select(r => r.Title)
            .ShouldBe(
                ["Review PR #482", "Renew passport", "Book flights to Berlin"],
                ignoreOrder: true
            );
        shell.ShowInspector.ShouldBeTrue();

        var deck = day.FlatRows.Single(r => r.Title.StartsWith("Prepare the Q4"));
        deck.Links =
        [
            LinkLine.From(
                "https://docs.example.com/q4-planning",
                new LinkPreview
                {
                    Url = "https://docs.example.com/q4-planning",
                    ProviderId = "web",
                    Title = "Q4 planning · Roadmap & hiring",
                    Subtitle = "docs.example.com",
                    ChipFacts = [new("Edited yesterday"), new("12 comments")],
                    Snippet =
                        "Goals for the quarter, the hiring plan by team, and what slips if we do not hire.",
                    Status = PreviewStatus.Loaded,
                },
                "Web link"
            ),
        ];
        await SnapAsync(window, "dayahead-dark");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await SnapAsync(window, "dayahead-light");

        window.GetVisualDescendants().OfType<DayAheadView>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<ItemRowView>().Count().ShouldBeGreaterThanOrEqualTo(8);
    }

    [AvaloniaFact]
    public async Task An_empty_day_still_offers_something_to_start_from()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Light);
        await shell.InitializeAsync();
        await shell.ShowDayAsync(AppFixture.Today.AddDays(2));

        var day = shell.Content.ShouldBeOfType<DayAheadViewModel>();
        day.IsEmpty.ShouldBeTrue();
        day.EmptyText.ShouldContain("Nothing is planned");
        day.Sections.Select(s => s.Title).ShouldBe(["Could go here"]);
        await SnapAsync(window, "dayahead-empty-light");
    }

    [AvaloniaFact]
    public async Task Plan_here_moves_a_suggestion_onto_the_day()
    {
        await SeedAsync();
        var (_, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.ShowDayAsync(Tomorrow);
        var day = shell.Content.ShouldBeOfType<DayAheadViewModel>();
        var flights = day.FlatRows.Single(r => r.Title == "Book flights to Berlin");

        await day.PlanHereCommand.ExecuteAsync(flights);
        await SettleAsync();

        (await _app.GetAsync(flights.Id)).PlannedFor.ShouldBe(Tomorrow);
        var again = shell.Content.ShouldBeOfType<DayAheadViewModel>();
        again
            .Sections.Single(s => s.Title == "Any time")
            .Rows.Select(r => r.Title)
            .ShouldContain("Book flights to Berlin");
    }

    [AvaloniaFact]
    public async Task The_week_shows_what_each_day_holds_and_opens_a_day_on_click()
    {
        await SeedAsync();
        var (window, shell) = Open(ThemeVariant.Dark);
        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Today);
        await shell.SetScopeCommand.ExecuteAsync("Week");
        var calendar = shell.Content.ShouldBeOfType<CalendarViewModel>();

        var tomorrow = calendar.Days.Single(d => d.Day == Tomorrow);
        tomorrow.HasMeter.ShouldBeTrue();
        tomorrow.FreeText.ShouldBe("3h 10m free");
        tomorrow.Rows.Single(r => r.Title.StartsWith("Prepare")).ShowDetail.ShouldBeTrue();
        calendar
            .Days.Single(d => d.Day == AppFixture.Today.AddDays(2))
            .OffersPlanDay.ShouldBeTrue();
        await SnapAsync(window, "week-stacked-dark");

        await shell.SetScopeCommand.ExecuteAsync("Week");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window.Width = 1500;
        await SnapAsync(window, "week-stacked-wide-light");

        calendar.OpenDay(Tomorrow);
        await SettleAsync();
        shell.Content.ShouldBeOfType<DayAheadViewModel>().Day.ShouldBe(Tomorrow);

        await shell.GoAsync(AppPage.Today);
        await shell.SetScopeCommand.ExecuteAsync("Month");
        await shell.OpenDayAsync(AppFixture.Today);
        shell.IsDayScope.ShouldBeTrue();
        shell.Content.ShouldBeOfType<TodayViewModel>();
    }

    [AvaloniaFact]
    public void Link_text_turns_urls_into_underlined_link_runs_and_leaves_plain_text_alone()
    {
        var plain = new LinkText { Value = "nothing to click here" };
        plain.Inlines!.Count.ShouldBe(0);
        plain.Text.ShouldBe("nothing to click here");

        var linked = new LinkText { Value = "see https://a.test/x, then rest" };
        var window = new Window { Content = linked };
        window.Show();
        var runs = linked.Inlines!.OfType<Run>().ToList();
        runs.Select(r => r.Text).ShouldBe(["see ", "https://a.test/x", ", then rest"]);
        runs[1].TextDecorations.ShouldNotBeNull();
        runs[0].TextDecorations.ShouldBeNull();
    }
}
