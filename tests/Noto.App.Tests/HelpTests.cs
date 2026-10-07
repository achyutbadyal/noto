using System.Reflection;
using Noto.App.ViewModels;
using Noto.App.Views;

namespace Noto.App.Tests;

// The in-app guide is the answer to "what does this mode/number even mean", so its deep links must
// never rot: every HelpTopicIds constant has a topic, every topic is declared, and every mode control
// the user can pick is described somewhere in the guide.
public sealed class HelpTests : IDisposable
{
    readonly AppFixture _app = new();
    readonly ShellViewModel _shell;

    public HelpTests() => _shell = new ShellViewModel(_app.Services);

    public void Dispose() => _app.Dispose();

    static IReadOnlyList<string> DeclaredIds() =>
        [
            .. typeof(HelpTopicIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!),
        ];

    static IEnumerable<HelpPoint> PointsOf(string topicId) =>
        HelpContent.Find(topicId)!.Sections.SelectMany(s => s.Points ?? []);

    [Fact]
    public void Every_deep_link_target_has_a_topic_and_every_topic_is_reachable()
    {
        var declared = DeclaredIds();
        var topics = HelpContent.Topics.Select(t => t.Id).ToList();

        declared.ShouldNotBeEmpty();
        topics.ShouldBe(declared, ignoreOrder: true);
        topics.Distinct().Count().ShouldBe(topics.Count); // no duplicate ids
    }

    [Fact]
    public void Every_topic_has_content_worth_reading()
    {
        foreach (var topic in HelpContent.Topics)
        {
            topic.Title.ShouldNotBeNullOrWhiteSpace();
            topic.Summary.ShouldNotBeNullOrWhiteSpace();
            topic.Sections.ShouldNotBeEmpty($"topic '{topic.Id}' has no sections");
            AppIcons
                .Has(topic.Icon)
                .ShouldBeTrue($"topic '{topic.Id}' uses unknown icon '{topic.Icon}'");
            foreach (var section in topic.Sections)
            {
                section.Heading.ShouldNotBeNullOrWhiteSpace();
                section.Body.ShouldNotBeNullOrWhiteSpace();
                foreach (var point in section.Points ?? [])
                {
                    point.Label.ShouldNotBeNullOrWhiteSpace();
                    point.Text.ShouldNotBeNullOrWhiteSpace();
                }
            }
        }
    }

    [Fact]
    public async Task Guide_page_opens_and_deep_links_to_a_topic()
    {
        await _shell.InitializeAsync();

        await _shell.ShowHelpTopicAsync(HelpTopicIds.Modes);

        _shell.Page.ShouldBe(AppPage.Help);
        _shell.HeaderTitle.ShouldBe("Guide");
        _shell.IsHelp.ShouldBeTrue();
        _shell.Content.ShouldBeSameAs(_shell.Help);
        _shell.Help!.Selected!.Id.ShouldBe(HelpTopicIds.Modes);

        // The sidebar entry opens the guide at the top.
        await _shell.OpenHelpCommand.ExecuteAsync(null);
        _shell.Help.Selected!.Id.ShouldBe(HelpTopicIds.Start);

        // An unknown id must land somewhere sensible rather than throwing or blanking the page.
        await _shell.ShowHelpTopicAsync("no-such-topic");
        _shell.Help.Selected!.Id.ShouldBe(HelpTopicIds.Start);
    }

    [Fact]
    public async Task Guide_search_filters_topics_and_reports_no_matches()
    {
        await _shell.InitializeAsync();
        await _shell.ShowHelpTopicAsync(HelpTopicIds.Start);
        var help = _shell.Help!;

        help.Filter = "kanban";
        help.Topics.Select(t => t.Id).ShouldContain(HelpTopicIds.Modes);
        help.Topics.Select(t => t.Id).ShouldNotContain(HelpTopicIds.Shortcuts);
        help.NoMatches.ShouldBeFalse();

        help.Filter = "swimlane";
        help.Topics.ShouldBeEmpty();
        help.NoMatches.ShouldBeTrue();

        help.Filter = "";
        help.Topics.Count.ShouldBe(HelpContent.Topics.Count);
        help.NoMatches.ShouldBeFalse();
    }

    [Fact]
    public async Task Every_mode_control_is_explained_by_the_guide()
    {
        await _shell.InitializeAsync();
        await _shell.GoAsync(AppPage.Settings);
        var settings = _shell.Settings!;

        // Every choice the dropdowns offer must be described in the guide, using the same words the
        // dropdown shows — that is the whole point of the feature.
        foreach (var preset in settings.Presets)
            PointsOf(HelpTopicIds.Modes)
                .Any(p => p.Label == preset.Name)
                .ShouldBeTrue($"preset '{preset.Name}' is not described in the guide");

        foreach (var option in settings.LayoutOptions)
            PointsOf(HelpTopicIds.Layouts)
                .Any(p => p.Label == option.Label)
                .ShouldBeTrue($"layout '{option.Label}' is not described in the guide");

        foreach (var option in settings.OrderOptions)
            PointsOf(HelpTopicIds.Order)
                .Any(p => p.Label == option.Label)
                .ShouldBeTrue($"order '{option.Label}' is not described in the guide");

        foreach (var option in settings.PressureOptions)
            PointsOf(HelpTopicIds.Pressure)
                .Any(p => p.Label == option.Label)
                .ShouldBeTrue($"pressure '{option.Label}' is not described in the guide");
    }
}
