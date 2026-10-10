using Noto.App.Services;
using Noto.Core.Models;

namespace Noto.App.ViewModels;

// The one place view-models get their dependencies. Each view-model asks only for the services it uses, and
// this factory (built from the host's AppServices) is where they're picked, so nothing reaches into a bag.
public sealed class ViewModelFactory(AppServices s)
{
    readonly ListServices _list = new(
        s.Reader,
        s.Runner,
        s.Focus,
        s.Clock,
        s.Suggestions,
        s.LinkResolver
    );

    public AppearanceViewModel Appearance() => new(s.UiState);

    public UndoToastViewModel Toast() => new(s.Undo);

    public CommandBarViewModel CommandBar(ICommandBarHost host) =>
        new(s.Search, s.Runner, host, s.Suggestions, s.LinkResolver);

    public InspectorViewModel Inspector() => new(s.Reader, s.Runner, s.Focus, Decisions());

    public OnboardingViewModel Onboarding() => new(s.Workspaces);

    public CaptureViewModel Capture() =>
        new(s.Clock, s.Platform, s.Workspaces, s.Links, s.Runner, s.Suggestions, s.LinkResolver);

    public TodayViewModel Today(Guid workspaceId, Func<string, Guid?>? resolveWorkspace = null) =>
        new(_list, workspaceId, resolveWorkspace);

    public BacklogViewModel Backlog(
        Guid workspaceId,
        Func<string, Guid?>? resolveWorkspace = null
    ) => new(_list, workspaceId, resolveWorkspace);

    public BoardViewModel Board(Guid workspaceId) => new(_list, s.Workspaces, workspaceId);

    public TimelineViewModel Timeline(Guid workspaceId) => new(_list, workspaceId);

    public HabitGridViewModel HabitGrid(Guid workspaceId) =>
        new(_list, s.Uow, s.Recurrence, workspaceId);

    // The home screen for a non-list layout.
    public ItemListViewModel Home(Layout layout, Guid workspaceId) =>
        layout switch
        {
            Layout.Board => Board(workspaceId),
            Layout.Timeline => Timeline(workspaceId),
            _ => HabitGrid(workspaceId),
        };

    public TodayAllViewModel TodayAll() => new(_list, s.TodayAll);

    public ReviewViewModel Review(Guid workspaceId, ReviewMode mode) =>
        new(s.Reader, s.Undo, s.UiState, s.DayNotes, Decisions(), workspaceId, mode);

    public ShutdownViewModel Shutdown(Guid workspaceId) =>
        new(s.Reader, s.DayNotes, Review(workspaceId, ReviewMode.Shutdown), workspaceId);

    public InsightsViewModel Insights(Guid workspaceId) => new(s.Reader, workspaceId);

    public DayLogViewModel DayLog(Guid workspaceId) => new(s.Reader, workspaceId);

    public WeeklyReviewViewModel WeeklyReview(Guid workspaceId) =>
        new(s.Reader, s.DayNoteService, s.Runner, workspaceId);

    public SettingsViewModel Settings(
        Guid workspaceId,
        AppearanceViewModel appearance,
        AccountViewModel? account
    ) =>
        new(
            s.Platform,
            s.Import,
            s.Export,
            s.Runner,
            s.Workspaces,
            workspaceId,
            appearance,
            account,
            s.Ai
        );

    DecisionController Decisions() => new(s.Runner);
}
