using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

// The guide's content. Kept as data so the same topics feed the page, the command bar and the tests
// (a test asserts every topic id referenced by a tooltip exists).
public static class HelpContent
{
    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new(
            HelpTopicIds.Start,
            "Start here",
            "What the pieces are and how a day flows through Noto.",
            "today",
            [
                new(
                    "The three things you need to know",
                    "Noto keeps you honest about what you actually finish. Everything else is detail.",
                    [
                        new(
                            "Workspace",
                            "A container for one area of your life. Each has its own items, mode and stats."
                        ),
                        new(
                            "Mode",
                            "How a workspace looks and pushes you. It never changes what is stored."
                        ),
                        new(
                            "Today",
                            "One logical day per workspace. It can start at 04:00 if your nights run late."
                        ),
                    ]
                ),
                new(
                    "The loop",
                    "Noto is built around a small daily rhythm rather than an endless list.",
                    [
                        new(
                            "Capture",
                            "⌘K or the field at the bottom of Today. Type a sentence; tokens like ~30m or !2 are understood."
                        ),
                        new(
                            "Morning review",
                            "Decide what today holds. Carried items are put in front of you, one at a time."
                        ),
                        new(
                            "Work",
                            "Pick a Now item with f. Only one at a time, so the day has a focus."
                        ),
                        new("Shutdown", "Close the day: say what got done and what carries."),
                        new("Weekly review", "Once a week, sweep what has gone stale."),
                    ]
                ),
                new(
                    "Where to go next",
                    "Press ⌘K and type a word — the guide itself is searchable, as is every screen.",
                    [
                        new(
                            "Confused by a control",
                            "Most controls have a small ⓘ next to them that opens the matching page here."
                        ),
                        new(
                            "Confused by a number",
                            "Open an item and read its Age, Carry and Defers — explained under \"Age, carry, defers\"."
                        ),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Workspaces,
            "Workspaces",
            "One container per area of your life.",
            "folder",
            [
                new(
                    "What a workspace isolates",
                    "Items, history and tags; the mode (layout, order, pressure); stats; sync; time zone, day boundary, focus hours and daily capacity.",
                    [
                        new(
                            "New workspace",
                            "Sidebar → New workspace. The templates (Work, Personal, Health, Side Projects) are one-click starting points."
                        ),
                        new("Switch", "Click it, or press ⌘1…⌘9."),
                        new("Rename / delete", "Settings → Workspace."),
                    ]
                ),
                new(
                    "Focus hours",
                    "Focus hours separate contexts in time as well as in data.",
                    [
                        new(
                            "Outside focus hours",
                            "The workspace drops out of Today (all) and its badge goes quiet. Nothing is hidden if you open it directly."
                        ),
                        new(
                            "Quick capture",
                            "Defaults to the workspace whose focus hours are active."
                        ),
                    ]
                ),
                new(
                    "Capacity and the day",
                    "Two settings that quietly shape what Today shows.",
                    [
                        new(
                            "Daily capacity",
                            "How much you can realistically take on. Today's capacity bar compares your planned work against it."
                        ),
                        new(
                            "Day boundary",
                            "When a new day begins, e.g. 04:00. Work done at 01:00 still counts for yesterday."
                        ),
                        new(
                            "Time zone",
                            "Follow the device, or pin the workspace to a zone so a trip does not reshuffle your days."
                        ),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Modes,
            "Modes, presets, layout, order, pressure",
            "A mode is three independent controls. The named modes are just bundles of them.",
            "settings",
            [
                new(
                    "A mode is three controls, not one",
                    "Earlier versions had six monolithic modes, which contradicted each other. A mode is now three independent controls, and the six named modes are presets over them.",
                    [
                        new(
                            "Layout",
                            "How items are arranged: list, board, timeline or habit grid."
                        ),
                        new("Order", "How items are sorted inside their section."),
                        new(
                            "Pressure",
                            "How loudly Noto reminds you: gentle, honest or relentless."
                        ),
                        new(
                            "Preset",
                            "A named bundle of the three. Picking one just sets all three at once."
                        ),
                    ]
                ),
                new(
                    "Changing a mode never edits your items",
                    "Switching layout, order or pressure is presentation only. It writes nothing to items, so you can experiment freely and switch back.",
                    [
                        new(
                            "Items without the layout's field",
                            "They are never dropped. A timeline item with no date sits in a \"No date\" lane; a board item with no column sits in Someday."
                        ),
                        new(
                            "Rollover",
                            "Identical in every mode, because rollover is a query over the same data."
                        ),
                        new(
                            "Custom",
                            "Change any one control and the preset reads \"sprint (custom)\". Change it back and the label follows."
                        ),
                    ]
                ),
                new(
                    "The six presets",
                    "Each is a starting point, not a commitment.",
                    [
                        new(
                            "Sprint",
                            "list · priority + carry · honest — work and daily throughput."
                        ),
                        new("Zen", "list (spacious) · manual · gentle — personal, low pressure."),
                        new(
                            "Deadline",
                            "timeline · due date · honest — exams, launches, time-boxed projects."
                        ),
                        new("Habit", "habit grid · time of day · gentle — health and routines."),
                        new(
                            "Kanban",
                            "board · manual · gentle — side projects and multi-phase work."
                        ),
                        new(
                            "Accountability",
                            "list · carry · relentless — any workspace where you want the truth."
                        ),
                    ]
                ),
                new(
                    "How to change it",
                    "Sidebar → Settings → Mode, or press ⌘K and type \"Switch preset\".",
                    null
                ),
            ]
        ),
        new(
            HelpTopicIds.Layouts,
            "Layouts",
            "List, board, timeline or habit grid.",
            "kanban",
            [
                new(
                    "What each layout does",
                    "The layout decides the shape of the home screen. The items are the same in all four.",
                    [
                        new(
                            "List",
                            "Sections Now / Planned / Waiting on / Done today, plus a Backlog for Unscheduled and Someday."
                        ),
                        new(
                            "Board",
                            "Columns Someday · Backlog · Today · Waiting · Done. Dragging sets status and planned date — no hidden extra state."
                        ),
                        new(
                            "Timeline",
                            "Items with a due date or a future plan are placed on a line; overdue items are pinned on top; the rest wait in \"No date\"."
                        ),
                        new(
                            "Habit grid",
                            "Shows repeating rules with missed days skipped. Everything else is one click away under \"Not habits\"."
                        ),
                    ]
                ),
                new(
                    "Each layout remembers its own setup",
                    "Board columns, timeline range and the grid's window are stored per layout, so switching away and back restores them.",
                    null
                ),
            ]
        ),
        new(
            HelpTopicIds.Order,
            "Order",
            "How items are sorted inside a section.",
            "sprint",
            [
                new(
                    "The five strategies",
                    "Sections (Now, Planned, Waiting, Done) are always applied first; order sorts within a section.",
                    [
                        new("Manual", "Keeps the order you dragged things into."),
                        new(
                            "Priority + carry",
                            "Priority first, then how long it has been carried."
                        ),
                        new(
                            "Due date",
                            "Overdue first, then nearest due date; undated items last."
                        ),
                        new("Carry", "Most-carried and oldest first — the honest view."),
                        new(
                            "Time of day",
                            "Morning → Midday → Afternoon → Evening, then your manual order."
                        ),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Pressure,
            "Pressure",
            "How loudly Noto reminds you — and how much it does on its own.",
            "accountability",
            [
                new(
                    "Carry, not age",
                    "Every threshold is measured in carry: the number of planned days that passed with the item undone. An item added today is fresh however old its due date is.",
                    [
                        new(
                            "Gentle",
                            "No left bar, review is optional. Fresh 0–4, warm 5–9, hot 10–13, stale 14+. Stuck at carry 14 or 5 defers."
                        ),
                        new(
                            "Honest",
                            "Review is on. Fresh 0, warm 1–2, hot 3–5, stale 6+. Stuck at carry 3 or 3 defers."
                        ),
                        new(
                            "Relentless",
                            "Review cannot be skipped and your 3 most-carried items are pinned on top. Stuck at carry 2 or 2 defers."
                        ),
                    ]
                ),
                new(
                    "What pressure never does",
                    "It does not change what is stored and it does not shame you. A missed day is an empty square, never a red cross.",
                    null
                ),
            ]
        ),
        new(
            HelpTopicIds.Carry,
            "Age, carry, defers",
            "The three numbers in the inspector, and what the colours mean.",
            "carry",
            [
                new(
                    "The three numbers",
                    "Select an item and they appear on the right. They are the honest core of Noto.",
                    [
                        new(
                            "Age",
                            "Days since the item was created. A plain fact; nothing escalates on age alone."
                        ),
                        new(
                            "Carry",
                            "Days the item was planned for and left undone. This is what pressure reacts to."
                        ),
                        new("Defers", "Times you explicitly pushed it to a later day."),
                    ]
                ),
                new(
                    "The states",
                    "The same item can be warm in one workspace and stale in another, because the thresholds are the workspace's pressure setting.",
                    [
                        new("Fresh", "Ordinary text colour. Nothing to see."),
                        new("Warm", "A gentle amber tint on the carry mark."),
                        new("Hot", "Amber bar on the left edge of the row."),
                        new("Stale", "Red bar, and the row says \"stuck?\"."),
                    ]
                ),
                new(
                    "\"Stuck?\" is a question, not a verdict",
                    "When an item crosses the stuck threshold, Noto asks why instead of nagging: too big, blocked, waiting, not important, or forgotten.",
                    [
                        new("Break it down", "Splits it into steps so the next action is obvious."),
                        new("Make it Now", "Commits the next 25 minutes to it."),
                        new(
                            "First thing tomorrow",
                            "A deliberate one-day defer — it will not be counted as carry twice."
                        ),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Editing,
            "Editing, deleting, restoring",
            "Change any field of a task, and get anything back.",
            "planned",
            [
                new(
                    "Everything is editable",
                    "Select a task and the inspector on the right lists its fields. Changing one runs the same action the keyboard does — nothing is a special case.",
                    [
                        new("Title", "Type a new one and press Enter, or click away."),
                        new("Time", "An estimate: 15 min, 30 min, 1 hr, 1.5 hr… It counts against today's capacity."),
                        new("Priority", "1 is highest. It decides the order when the workspace sorts by priority."),
                        new("When", "Today, tomorrow, in a week, Someday, or Unscheduled."),
                        new("Due", "A deadline. Overdue items are pinned on top in the Deadline layout."),
                        new("Day part", "Morning, midday, afternoon or evening — used by the habit grid's order."),
                        new("Waiting on", "Name who or what is blocking it; the task moves to Waiting on."),
                        new("Notes", "Free markdown."),
                    ]
                ),
                new(
                    "Every change is recorded",
                    "Each edit is written to the item's history, which is what \"Life of this item\" at the bottom of the inspector shows — with the values, not just that something changed:",
                    [
                        new("Example", "estimate 30m → 1h · priority none → P2 · due Oct 12 → no date"),
                        new("Why keep it", "It is how Noto answers \"why is this still here?\" without guessing."),
                        new("Undo", "Every change is one ⌘Z. Nothing here needs a confirmation dialog."),
                    ]
                ),
                new(
                    "Deleting",
                    "Nothing is ever destroyed outright.",
                    [
                        new("Delete a task", "Inspector → Delete task. The row leaves your lists, and ⌘Z brings it straight back."),
                        new("Drop is different", "Dropping (⌫) keeps the task and records why you let it go; deleting hides it entirely."),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Review,
            "Morning review",
            "The one-at-a-time decision pass that starts your day.",
            "review",
            [
                new(
                    "What it is",
                    "Noto puts every carried item in front of you, one at a time, and asks for a decision. It exists because a list you never decide on is just a wish.",
                    [
                        new(
                            "When it opens",
                            "Once a day, unless the workspace is on gentle pressure or you dismissed it. ⌘⇧R opens it any time."
                        ),
                        new("Nothing to review", "It tells you so and gets out of the way."),
                        new("Undo", "Every decision is undoable — ⌘Z."),
                    ]
                ),
                new(
                    "The decisions",
                    "Each has a single key, so the whole review is one hand on the keyboard.",
                    [
                        new("T — Today", "Commit it to today."),
                        new("D — Defer", "Push it to a specific day."),
                        new("S — Someday", "Off today's plate, still alive."),
                        new("B — Break down", "Turn it into smaller steps."),
                        new("W — Waiting on", "Blocked on someone; asks who."),
                        new("X — Drop", "Let it go. It is not a failure."),
                        new("A — Already done", "It happened; record it without carrying it."),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Shutdown,
            "Shutdown",
            "The three-step end of the day.",
            "moon",
            [
                new(
                    "Why it exists",
                    "The day ends deliberately rather than by running out of attention. Shutdown records what happened and what carries, then closes.",
                    [
                        new("Step 1", "What got done today."),
                        new("Step 2", "What is still open, and where it goes."),
                        new("Step 3", "One line about the day, kept for the weekly review."),
                        new("Later", "Press ⌘⇧D to run it whenever you are stopping."),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Weekly,
            "Weekly review",
            "The once-a-week sweep for things that have gone quiet.",
            "weekly",
            [
                new(
                    "Five steps",
                    "It looks back at the week and forward to the next, and gives you one place to clear the backlog.",
                    [
                        new("Completed", "What you actually finished."),
                        new(
                            "Someday",
                            "Promote it, keep it, or drop it. Anything untouched for 60+ days is pre-selected for dropping."
                        ),
                        new(
                            "Stuck",
                            "Grouped by why it stalled, so you can fix the cause rather than each item."
                        ),
                        new("Estimate vs focus", "How well your time estimates held up."),
                        new(
                            "Outcomes",
                            "Up to three outcomes for next week. They are pinned to the header all week."
                        ),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.TodayAll,
            "Today (all)",
            "Every workspace's today in one list, and why some disappear.",
            "todayall",
            [
                new(
                    "The cross-workspace lens",
                    "⌘0 shows today across all workspaces at once. Rows carry their workspace's colour so you can tell them apart.",
                    [
                        new(
                            "Why a workspace is missing",
                            "It is outside its focus hours, so it is quiet. Open it directly and everything is still there."
                        ),
                        new(
                            "Capacity",
                            "Committed time against capacity, summed across the workspaces that are in focus."
                        ),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Capture,
            "Capture",
            "Get it out of your head in one line.",
            "plus",
            [
                new(
                    "Where to capture",
                    "⌘K from anywhere, or the field at the bottom of Today. The same tokens work in both.",
                    [
                        new("~30m", "An estimate in minutes (also ~2h)."),
                        new("!2", "Priority 1–4."),
                        new("tomorrow / fri / 12 Oct", "A planned or due day."),
                        new("@waiting:name", "Mark it as waiting on someone."),
                        new("in Personal", "File it in another workspace by name."),
                        new("#tag", "A tag."),
                    ]
                ),
                new(
                    "Attach what you are looking at",
                    "When a browser is in front, quick capture offers to attach the current page to the item.",
                    null
                ),
            ]
        ),
        new(
            HelpTopicIds.Shortcuts,
            "Keyboard shortcuts",
            "The keys worth knowing. Press ? for this list at any time.",
            "help",
            [
                new(
                    "Global",
                    "These work from anywhere except while typing in a text field.",
                    [
                        new("⌘K", "Ask or jump — search items, run commands, or add one."),
                        new("⌘1…⌘9", "Switch workspace."),
                        new("⌘0", "Today across all workspaces."),
                        new("⌘T / ⌘⇧R / ⌘⇧D", "Jump to today / start review / shut down."),
                        new("⌘I / ⌘B", "Toggle the inspector / the sidebar."),
                        new("⌘Z", "Undo the last action."),
                        new("⌘⇧M", "Open the mode settings."),
                    ]
                ),
                new(
                    "In a list",
                    "Move with j/k or the arrow keys.",
                    [
                        new("x", "Complete."),
                        new("e or Enter", "Edit the title."),
                        new("f", "Make it Now."),
                        new("t / d / s / w", "Today / Defer / Someday / Waiting on."),
                        new("b / ⌫", "Break down / drop."),
                        new("1–4", "Set priority. ~ sets an estimate."),
                        new("[ / ]", "Previous / next day."),
                    ]
                ),
            ]
        ),
        new(
            HelpTopicIds.Connected,
            "Connected apps",
            "Why a row can show \"2 approved\" or \"CI passing\".",
            "side",
            [
                new(
                    "What the chips are",
                    "When an item links to a pull request or issue, Noto shows a few facts from the host as chips on the row. They are read-only — a glance, not a control panel.",
                    [
                        new("Approvals", "How many reviewers have approved."),
                        new("Checks", "The CI result, if the host reports one."),
                        new("State", "Open, ready to merge, or closed."),
                        new(
                            "Reconnect",
                            "If a token expires the chip says so; reconnecting is done from Settings."
                        ),
                    ]
                ),
            ]
        ),
    ];

    public static HelpTopic? Find(string? id) =>
        id is null ? null : Topics.FirstOrDefault(t => t.Id == id);
}

// The in-app guide (docs/07): one page that explains the concepts the UI cannot explain on its own.
// Tooltips and ⓘ buttons call Open(topicId) so a confused user lands on the exact page they need.
public sealed partial class HelpViewModel : ObservableObject
{
    public HelpViewModel(string? topicId = null)
    {
        Topics = new ObservableCollection<HelpTopic>(HelpContent.Topics);
        Open(topicId);
    }

    public ObservableCollection<HelpTopic> Topics { get; }

    [ObservableProperty]
    HelpTopic? _selected;

    [ObservableProperty]
    string _filter = "";

    [ObservableProperty]
    bool _noMatches;

    [RelayCommand]
    void Select(HelpTopic topic) => Selected = topic;

    partial void OnFilterChanged(string value)
    {
        var query = value.Trim();
        Topics.Clear();
        foreach (var topic in HelpContent.Topics.Where(t => Matches(t, query)))
            Topics.Add(topic);
        NoMatches = Topics.Count == 0;
        if (!Topics.Contains(Selected ?? HelpContent.Topics[0]))
            Selected = Topics.FirstOrDefault();
    }

    // Deep link: select a topic by id (used by tooltips, ⓘ buttons and the command bar).
    public void Open(string? topicId)
    {
        Selected = HelpContent.Find(topicId) ?? Topics.FirstOrDefault();
    }

    static bool Matches(HelpTopic topic, string query)
    {
        if (query.Length == 0)
            return true;
        if (Contains(topic.Title, query) || Contains(topic.Summary, query))
            return true;
        foreach (var section in topic.Sections)
        {
            if (Contains(section.Heading, query) || Contains(section.Body, query))
                return true;
            if (section.Points is null)
                continue;
            foreach (var point in section.Points)
                if (Contains(point.Label, query) || Contains(point.Text, query))
                    return true;
        }
        return false;
    }

    static bool Contains(string haystack, string query) =>
        haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
}
