using System.Reflection;
using System.Text.Json;

namespace Noto.App.ViewModels;

// One "label — what it means" pair. Rendered as a definition list, which is the shape that makes
// vocabulary (layout names, carry, defers…) easy to look up.
public sealed record HelpPoint(string Label, string Text);

// A titled block of prose plus optional vocabulary.
public sealed record HelpSection(
    string Heading,
    string Body,
    IReadOnlyList<HelpPoint>? Points = null
);

// A page in the in-app guide. Ids are stable and are what tooltips and info buttons deep-link to.
public sealed record HelpTopic(
    string Id,
    string Title,
    string Summary,
    string Icon,
    IReadOnlyList<HelpSection> Sections
);

// The deep-link targets, as constants. XAML references these through x:Static, so a tooltip that
// points at a topic which does not exist is a compile error rather than a dead link.
public static class HelpTopicIds
{
    public const string Start = "start";
    public const string Workspaces = "workspaces";
    public const string Modes = "modes";
    public const string Layouts = "layouts";
    public const string Order = "order";
    public const string Pressure = "pressure";
    public const string Carry = "carry";
    public const string Editing = "editing";
    public const string Review = "review";
    public const string Shutdown = "shutdown";
    public const string Weekly = "weekly";
    public const string TodayAll = "today-all";
    public const string Capture = "capture";
    public const string Shortcuts = "shortcuts";
    public const string Connected = "connected";
}

// The guide's content lives in Help/topics.json (an embedded resource) so editing the guide never touches code.
// The same topics feed the page, the command bar and the tests (a test asserts every topic id referenced by a
// tooltip exists).
public static class HelpContent
{
    const string Resource = "Noto.App.Help.topics.json";

    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<HelpTopic> Topics { get; } = Load();

    public static HelpTopic? Find(string? id) =>
        id is null ? null : Topics.FirstOrDefault(t => t.Id == id);

    static IReadOnlyList<HelpTopic> Load()
    {
        using var stream =
            typeof(HelpContent).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Missing embedded resource {Resource}");
        return JsonSerializer.Deserialize<List<HelpTopic>>(stream, Options)
            ?? throw new InvalidOperationException("Help topics are empty");
    }
}
