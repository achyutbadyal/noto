using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Layouts;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Core.Time;
using Noto.Core.Workspaces;

namespace Noto.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject, ICommandBarHost
{
    readonly AppServices _services;
    readonly Dictionary<Guid, TodayViewModel> _today = [];
    readonly Dictionary<Guid, BacklogViewModel> _backlogs = [];
    readonly Dictionary<Guid, ItemListViewModel> _layoutPages = [];
    readonly SemaphoreSlim _refreshGate = new(1, 1);
    DateOnly _todayDate;
    bool _pendingG;

    public ShellViewModel(AppServices services)
    {
        _services = services;
        Appearance = new AppearanceViewModel(services.UiState);
        Toast = new UndoToastViewModel(services.Undo);
        CommandBar = new CommandBarViewModel(services, this);
        Inspector = new InspectorViewModel(services);
        Inspector.Decisions.PropertyChanged += (_, e) => ForwardMessage(e, Inspector.Decisions);
        services.Runner.Changed += () => _ = RefreshAsync();
    }

    public AppServices Services => _services;
    public AppearanceViewModel Appearance { get; }
    public UndoToastViewModel Toast { get; }
    public CommandBarViewModel CommandBar { get; }
    public InspectorViewModel Inspector { get; }
    public ObservableCollection<WorkspaceTabViewModel> Workspaces { get; } = [];

    [ObservableProperty]
    WorkspaceTabViewModel? _selected;

    [
        ObservableProperty,
        NotifyPropertyChangedFor(
            nameof(ShowInspector),
            nameof(IsListPage),
            nameof(IsToday),
            nameof(IsBacklog),
            nameof(IsTodayAll),
            nameof(IsHelp)
        )
    ]
    AppPage _page = AppPage.Today;

    [ObservableProperty]
    object? _content;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowInspector))]
    bool _isInspectorOpen = true;

    [
        ObservableProperty,
        NotifyPropertyChangedFor(
            nameof(SidebarWidth),
            nameof(EffectiveSidebarExpanded),
            nameof(IsSidebarCollapsed)
        )
    ]
    bool _isSidebarExpanded = true;

    [ObservableProperty]
    bool _isHelpOpen;

    [ObservableProperty]
    string _headerTitle = "";

    [ObservableProperty]
    string? _focusText;

    [ObservableProperty]
    DateOnly? _viewDay;

    // Viewport-driven layout: the window pushes its width here so the shell can adapt instead of clipping.
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsCompact),
        nameof(IsNarrow),
        nameof(ShowToolbarLabels),
        nameof(EffectiveSidebarExpanded),
        nameof(IsSidebarCollapsed),
        nameof(SidebarWidth),
        nameof(ShowInspector)
    )]
    double _viewportWidth = 1240;

    // Below ~880px the sidebar collapses to its icon rail and the toolbar drops its labels.
    public bool IsCompact => ViewportWidth < 880;

    // Below ~1000px the inspector is hidden so the list keeps a usable width.
    public bool IsNarrow => ViewportWidth < 1000;
    public bool ShowToolbarLabels => !IsCompact;

    public bool EffectiveSidebarExpanded => IsSidebarExpanded && !IsCompact;

    // True whenever the sidebar is showing its icon rail (either user-collapsed or auto-collapsed).
    public bool IsSidebarCollapsed => !EffectiveSidebarExpanded;

    public bool ShowInspector =>
        IsInspectorOpen
        && !IsNarrow
        && Page is AppPage.Today or AppPage.Backlog or AppPage.TodayAll;
    public double SidebarWidth => EffectiveSidebarExpanded ? 232 : 56;
    public bool IsListPage =>
        Page is AppPage.Today or AppPage.Backlog or AppPage.DayLog or AppPage.TodayAll;
    public bool IsToday => Page == AppPage.Today;
    public bool IsBacklog => Page == AppPage.Backlog;
    public bool IsTodayAll => Page == AppPage.TodayAll;
    public bool IsHelp => Page == AppPage.Help;

    public TodayViewModel? TodayPage => Selected is { } s ? _today.GetValueOrDefault(s.Id) : null;
    public ReviewViewModel? Review { get; private set; }
    public ShutdownViewModel? Shutdown { get; private set; }
    public InsightsViewModel? Insights { get; private set; }
    public SettingsViewModel? Settings { get; private set; }
    public DayLogViewModel? DayLog { get; private set; }
    public OnboardingViewModel? Onboarding { get; private set; }
    public TodayAllViewModel? TodayAllPage { get; private set; }
    public WeeklyReviewViewModel? WeeklyReview { get; private set; }
    public HelpViewModel? Help { get; private set; }
    public IReadOnlyList<string> Templates { get; } =
        Noto.Core.Workspaces.WorkspaceTemplates.All.Select(t => t.Name).ToList();

    [ObservableProperty]
    string? _weekOutcomes;

    public IReadOnlyList<WorkspaceRef> WorkspaceRefs =>
        Workspaces.Select(w => new WorkspaceRef(w.Id, w.Name)).ToList();
    public Guid? CurrentWorkspaceId => Selected?.Id;
    public DateOnly Today =>
        _todayDate == default
            ? DateOnly.FromDateTime(_services.Clock.UtcNow.UtcDateTime)
            : _todayDate;
    public DateOnly CurrentDay => ViewDay ?? Today;
    public IReadOnlyList<Binding> Shortcuts => KeyMap.All;

    // ---- startup & workspaces ----

    public async Task InitializeAsync()
    {
        var all = await _services.Workspaces.ListAsync();
        if (all.Count == 0)
        {
            ShowOnboarding();
            return;
        }

        await RebuildTabsAsync(all);
        await SelectWorkspaceAsync(Workspaces[0].Id);
    }

    void ShowOnboarding()
    {
        Onboarding = new OnboardingViewModel(_services);
        Onboarding.Completed += () => _ = InitializeAsync();
        Page = AppPage.Onboarding;
        Content = Onboarding;
        HeaderTitle = "Welcome";
    }

    async Task RebuildTabsAsync(IReadOnlyList<Workspace>? all = null)
    {
        all ??= await _services.Workspaces.ListAsync();
        var selectedId = Selected?.Id;
        Workspaces.Clear();
        for (var i = 0; i < all.Count; i++)
        {
            var ws = all[i];
            var tab = new WorkspaceTabViewModel(ws.Id, ws.Name, ws.Icon, ws.Color, i)
            {
                NeedsDecision = await _services.Reader.NeedsDecisionCountAsync(ws.Id),
                IsQuiet = !Noto.Core.Workspaces.FocusHours.IsActive(ws, _services.Clock),
                IsSelected = ws.Id == selectedId,
            };
            Workspaces.Add(tab);
        }
        Selected = Workspaces.FirstOrDefault(w => w.Id == selectedId);
    }

    public async Task SelectWorkspaceAsync(Guid id) =>
        await SelectWorkspaceAsync(id, allowAutoReview: true);

    async Task SelectWorkspaceAsync(Guid id, bool allowAutoReview)
    {
        var tab =
            Workspaces.FirstOrDefault(w => w.Id == id)
            ?? throw new InvalidOperationException("Unknown workspace");
        foreach (var w in Workspaces)
            w.IsSelected = w == tab;
        Selected = tab;
        ViewDay = null;
        Settings = null;
        Insights = null;

        await _services.Recurrence.GenerateDueAsync(id);
        var today = TodayFor(id);
        await today.ReloadAsync();
        _todayDate = today.Snapshot!.Today;
        Page = AppPage.Today;
        Content = await HomeContentAsync(today);
        await RefreshOutcomesAsync();
        await UpdateInspectorAsync();
        UpdateHeader();

        // The morning review opens itself once per day, unless the user chose gentle pressure or already dismissed it.
        if (
            allowAutoReview
            && today.NeedsDecision > 0
            && today.Snapshot.Workspace.Pressure != Pressure.Gentle
            && _services.UiState.Get(ReviewViewModel.DismissKey(id))
                != today.Snapshot.Today.ToString("yyyy-MM-dd")
        )
            await StartReviewAsync();
    }

    // The workspace's home screen follows its layout; the list layout is the Today view itself.
    async Task<object> HomeContentAsync(TodayViewModel today)
    {
        var ws = today.Snapshot!.Workspace;
        if (ws.Layout == Layout.List)
            return today;

        if (!_layoutPages.TryGetValue(ws.Id, out var page) || !MatchesLayout(page, ws.Layout))
        {
            page = ws.Layout switch
            {
                Layout.Board => new BoardViewModel(_services, ws.Id),
                Layout.Timeline => new TimelineViewModel(_services, ws.Id),
                _ => new HabitGridViewModel(_services, ws.Id),
            };
            Attach(page);
            _layoutPages[ws.Id] = page;
        }
        await page.ReloadAsync();
        return page;
    }

    static bool MatchesLayout(ItemListViewModel page, Layout layout) =>
        layout switch
        {
            Layout.Board => page is BoardViewModel,
            Layout.Timeline => page is TimelineViewModel,
            Layout.HabitGrid => page is HabitGridViewModel,
            _ => false,
        };

    bool HomeMatchesLayout(Workspace ws) =>
        ws.Layout == Layout.List
            ? Content == TodayPage
            : Content is ItemListViewModel list && MatchesLayout(list, ws.Layout);

    async Task RefreshOutcomesAsync()
    {
        if (Selected is not { } ws)
        {
            WeekOutcomes = null;
            return;
        }
        var outcomes = await _services.DayNoteService.GetOutcomesAsync(ws.Id, Today);
        WeekOutcomes = outcomes.Count == 0 ? null : "This week: " + string.Join(" · ", outcomes);
    }

    TodayViewModel TodayFor(Guid id)
    {
        if (_today.TryGetValue(id, out var existing))
            return existing;
        var vm = new TodayViewModel(_services, id, ResolveWorkspaceName);
        Attach(vm);
        vm.Add.Added += () => _ = RefreshAsync();
        return _today[id] = vm;
    }

    BacklogViewModel BacklogFor(Guid id)
    {
        if (_backlogs.TryGetValue(id, out var existing))
            return existing;
        var vm = new BacklogViewModel(_services, id, ResolveWorkspaceName);
        Attach(vm);
        return _backlogs[id] = vm;
    }

    void Attach(ItemListViewModel vm)
    {
        vm.FocusChanged += _ignored =>
        {
            if (Content == vm)
                _ = UpdateInspectorAsync();
        };
        vm.Decisions.PropertyChanged += (_, e) => ForwardMessage(e, vm.Decisions);
    }

    void ForwardMessage(PropertyChangedEventArgs e, DecisionController decisions)
    {
        if (
            e.PropertyName == nameof(DecisionController.Message)
            && decisions.Message is { } message
        )
            Toast.Show(message, canUndo: false);
    }

    Guid? ResolveWorkspaceName(string name) =>
        Workspaces.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id;

    // ---- navigation ----

    [RelayCommand]
    Task GoToAsync(string page) => GoAsync(Enum.Parse<AppPage>(page));

    [RelayCommand]
    Task PickWorkspaceAsync(WorkspaceTabViewModel tab) => SelectWorkspaceAsync(tab.Id);

    [RelayCommand]
    Task StepDayAsync(string delta) => ShiftDayAsync(int.Parse(delta));

    [RelayCommand]
    Task PickDayAsync(DayBarViewModel bar) => ShowDayAsync(bar.Day);

    [RelayCommand]
    Task OpenCommandBarAsync() => CommandBar.OpenAsync();

    [RelayCommand]
    Task UndoLastAsync() => UndoAsync();

    // "New workspace" offers the built-in templates as one click (docs/03).
    [RelayCommand]
    async Task NewWorkspaceAsync(string templateName)
    {
        var template = Noto.Core.Workspaces.WorkspaceTemplates.All.First(t =>
            t.Name == templateName
        );
        var ws = await _services.Workspaces.CreateFromTemplateAsync(template, Workspaces.Count);
        await RebuildTabsAsync();
        await SelectWorkspaceAsync(ws.Id, allowAutoReview: false);
    }

    [RelayCommand]
    void CloseHelp() => IsHelpOpen = false;

    // Deep link into the in-app guide: every tooltip's "learn more" and every ⓘ button lands here.
    [RelayCommand]
    Task OpenHelpAsync(string? topicId) => ShowHelpTopicAsync(topicId ?? HelpTopicIds.Start);

    public async Task ShowHelpTopicAsync(string topicId)
    {
        Help ??= new HelpViewModel();
        Help.Open(topicId);
        IsHelpOpen = false;
        await GoAsync(AppPage.Help);
    }

    [RelayCommand]
    Task StartReviewFromUiAsync() => StartReviewAsync();

    public async Task GoAsync(AppPage page)
    {
        if (Selected is not { } ws)
            return;
        // Leaving a page closes its half-finished capture panel rather than restoring it later.
        if (page != Page && Content is TodayViewModel leaving)
            leaving.Add.Dismiss(keepTitle: true);
        Settings = null;
        switch (page)
        {
            case AppPage.Today:
                ViewDay = null;
                await SelectWorkspaceAsync(ws.Id, allowAutoReview: false);
                return;
            case AppPage.Backlog:
                var backlog = BacklogFor(ws.Id);
                await backlog.ReloadAsync();
                Content = backlog;
                break;
            case AppPage.Review:
                await StartReviewAsync();
                return;
            case AppPage.Shutdown:
                Shutdown = new ShutdownViewModel(_services, ws.Id);
                Shutdown.Finished += () => _ = GoAsync(AppPage.Today);
                await Shutdown.LoadAsync();
                Content = Shutdown;
                break;
            case AppPage.Insights:
                Insights = new InsightsViewModel(_services, ws.Id);
                await Insights.LoadAsync();
                Content = Insights;
                break;
            case AppPage.TodayAll:
                TodayAllPage = new TodayAllViewModel(_services);
                Attach(TodayAllPage);
                await TodayAllPage.ReloadAsync();
                Content = TodayAllPage;
                break;
            case AppPage.WeeklyReview:
                WeeklyReview = new WeeklyReviewViewModel(_services, ws.Id);
                WeeklyReview.Finished += () => _ = GoAsync(AppPage.Today);
                await WeeklyReview.LoadAsync();
                Content = WeeklyReview;
                break;
            case AppPage.Settings:
                Settings = new SettingsViewModel(_services, ws.Id, Appearance);
                await Settings.LoadAsync();
                Content = Settings;
                break;
            case AppPage.Help:
                // Kept across visits so a search or a half-read topic survives navigation.
                Help ??= new HelpViewModel();
                Content = Help;
                break;
            default:
                return;
        }
        Page = page;
        await UpdateInspectorAsync();
        UpdateHeader();
    }

    public async Task StartReviewAsync()
    {
        if (Selected is not { } ws)
            return;
        var review = new ReviewViewModel(_services, ws.Id, ReviewMode.Morning);
        await review.LoadAsync();
        if (review.Entries.Count == 0)
        {
            Toast.Show("Nothing to review. You're all caught up.", canUndo: false);
            return;
        }

        Review = review;
        review.Closed += () => _ = GoAsync(AppPage.Today);
        review.Decisions.PropertyChanged += (_, e) => ForwardMessage(e, review.Decisions);
        Page = AppPage.Review;
        Content = review;
        UpdateHeader();
    }

    // [ / ] and day-strip clicks. Past days are a read-only log; future days list what's scheduled.
    public async Task ShowDayAsync(DateOnly day)
    {
        if (Selected is not { } ws)
            return;
        if (day == Today)
        {
            await GoAsync(AppPage.Today);
            return;
        }

        ViewDay = day;
        DayLog = new DayLogViewModel(_services, ws.Id);
        await DayLog.LoadAsync(day);
        Page = AppPage.DayLog;
        Content = DayLog;
        UpdateHeader();
    }

    public Task ShiftDayAsync(int delta) => ShowDayAsync(CurrentDay.AddDays(delta));

    public async Task OpenItemAsync(Guid workspaceId, Guid itemId)
    {
        if (Selected?.Id != workspaceId)
            await SelectWorkspaceAsync(workspaceId, allowAutoReview: false);
        else
            await GoAsync(AppPage.Today);

        var today = TodayPage!;
        var row = today.FlatRows.FirstOrDefault(r => r.Id == itemId);
        if (row is null)
        {
            // Not in Today (backlog, done earlier…): show it in the Backlog list instead.
            await GoAsync(AppPage.Backlog);
            var backlog = BacklogFor(workspaceId);
            backlog.SetFocus(backlog.FlatRows.FirstOrDefault(r => r.Id == itemId));
            return;
        }
        foreach (var section in today.Sections)
            section.IsCollapsed = section.IsCollapsed && !section.Rows.Contains(row);
        today.SetFocus(row);
    }

    [RelayCommand]
    public void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

    [RelayCommand]
    public void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    public void ShowHelp() => IsHelpOpen = true;

    public async Task ApplyPresetAsync(Preset preset)
    {
        if (Selected is not { } ws)
            return;
        await _services.Workspaces.UpdateAsync(ws.Id, preset.ApplyTo);
        _services.Runner.NotifyChanged();
        Toast.Show($"Switched to {preset.Name}", canUndo: false);
    }

    public async Task SetPressureAsync(Pressure pressure)
    {
        if (Selected is not { } ws)
            return;
        await _services.Workspaces.UpdateAsync(ws.Id, w => w.Pressure = pressure);
        _services.Runner.NotifyChanged();
    }

    public async Task UndoAsync()
    {
        if (Page == AppPage.Review && Review is not null && await Review.UndoAsync())
        {
            await RefreshTabsOnlyAsync();
            return;
        }
        if (await _services.Undo.UndoLastAsync() is null)
            Toast.Show("Nothing to undo", canUndo: false);
        await RefreshAsync();
    }

    // ---- refresh ----

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            await RebuildTabsAsync();
            var home = TodayPage;
            // The home snapshot also tells us the current layout, so it is always fresh before routing.
            if (home is not null && (Content != home || Page == AppPage.Today))
                await home.ReloadAsync();
            if (
                Page == AppPage.Today
                && home is { Snapshot: { } hs }
                && !HomeMatchesLayout(hs.Workspace)
            )
                Content = await HomeContentAsync(home);
            switch (Content)
            {
                case ItemListViewModel list when list != home:
                    await list.ReloadAsync();
                    break;
                case InsightsViewModel insights:
                    await insights.LoadAsync();
                    break;
                case DayLogViewModel log:
                    await log.LoadAsync(log.Day);
                    break;
            }
            if (TodayPage?.Snapshot is { } snap)
                _todayDate = snap.Today;

            // The Now item finished or changed under the timer: stop timing it.
            if (
                _services.Focus is { IsActive: true } focus
                && TodayPage?.Snapshot?.Find(focus.ItemId!.Value) is { Status: not ItemStatus.Open }
            )
                await focus.StopAsync();

            await RefreshOutcomesAsync();
            await UpdateInspectorAsync();
            UpdateHeader();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    async Task RefreshTabsOnlyAsync()
    {
        await RebuildTabsAsync();
        UpdateHeader();
    }

    async Task UpdateInspectorAsync()
    {
        if (Content is ItemListViewModel { FocusedSnapshot: { } snap } list)
            await Inspector.LoadAsync(list.FocusedRow?.Id, snap);
        else if (TodayPage?.Snapshot is { } todaySnap)
            await Inspector.LoadAsync(null, todaySnap);
    }

    void UpdateHeader() =>
        HeaderTitle = Page switch
        {
            AppPage.Today =>
                $"Today · {Today.ToString("ddd MMM d", System.Globalization.CultureInfo.InvariantCulture)}",
            AppPage.Backlog => "Backlog",
            AppPage.Review => "Morning review",
            AppPage.Shutdown => "Shutdown",
            AppPage.Insights => "Insights",
            AppPage.Settings => "Settings",
            AppPage.DayLog => DayLog?.Title ?? "",
            AppPage.TodayAll => "Today · all workspaces",
            AppPage.WeeklyReview => "Weekly review",
            AppPage.Help => "Guide",
            _ => "Welcome",
        };

    // Called by the host once a minute: day change banner and the Now timer.
    public async Task TickAsync()
    {
        FocusText = _services.Focus.IsActive
            ? $"{_services.Focus.Title} · {_services.Focus.RemainingText}"
            : null;
        if (Selected is not { } ws || TodayPage?.Snapshot is null)
            return;

        var tab = await _services.Workspaces.GetAsync(ws.Id);
        if (tab is null || LogicalDate.Today(tab, _services.Clock) == _todayDate)
            return;

        await _services.Recurrence.GenerateDueAsync(ws.Id);
        await RefreshAsync();
        var needs = TodayPage.NeedsDecision;
        Toast.Show(
            needs > 0
                ? $"It's a new day. {needs} item{(needs == 1 ? "" : "s")} to decide."
                : "It's a new day.",
            canUndo: false
        );
    }

    // ---- keyboard ----

    // Returns true when the key was consumed. `textInputFocused` lets single-key shortcuts stay out of text boxes.
    public async Task<bool> HandleKeyAsync(KeyChord chord, bool textInputFocused = false)
    {
        if (CommandBar.IsOpen)
            return await CommandBar.HandleKeyAsync(chord);
        if (IsHelpOpen)
        {
            if (chord.Key is "Escape" or "?")
                IsHelpOpen = false;
            return true;
        }

        // The detailed create panel is modal, so Escape must close it wherever focus happens to be
        // (a dropdown inside it swallows the key otherwise).
        if (chord.Key == "Escape" && Content is TodayViewModel { Add.IsDetailedOpen: true } detailed)
        {
            detailed.Add.Dismiss(keepTitle: true);
            return true;
        }

        if (Page == AppPage.Onboarding)
            return false;

        if (chord.Command && KeyMap.Resolve(KeyScope.Global, chord) is { } global)
        {
            // Inside a text box ⌘Z/⌘A belong to the box.
            if (!(textInputFocused && global.Action is AppAction.Undo or AppAction.SelectAll))
            {
                await ExecuteGlobalAsync(global);
                return true;
            }
        }

        if (await RoutePromptKeyAsync(chord))
            return true;
        if (textInputFocused)
            return false;

        // `g` then `b` / `t`: go to Backlog / Today.
        if (_pendingG)
        {
            _pendingG = false;
            if (chord.Key == "b")
            {
                await GoAsync(AppPage.Backlog);
                return true;
            }
            if (chord.Key == "t")
            {
                await GoAsync(AppPage.Today);
                return true;
            }
        }
        if (
            chord is { Key: "g", Command: false, Shift: false }
            && Page is AppPage.Today or AppPage.Backlog or AppPage.TodayAll
        )
        {
            _pendingG = true;
            return true;
        }

        if (Page is AppPage.Today or AppPage.Backlog or AppPage.DayLog or AppPage.TodayAll)
        {
            switch (KeyMap.Resolve(KeyScope.List, chord)?.Action)
            {
                case AppAction.PrevDay:
                    await ShiftDayAsync(-1);
                    return true;
                case AppAction.NextDay:
                    await ShiftDayAsync(1);
                    return true;
                case AppAction.Help:
                    IsHelpOpen = true;
                    return true;
                case AppAction.Search:
                    await CommandBar.OpenAsync();
                    return true;
            }
        }

        return Content switch
        {
            ReviewViewModel review => await review.HandleKeyAsync(chord),
            ShutdownViewModel shutdown => await shutdown.HandleKeyAsync(chord),
            WeeklyReviewViewModel weekly => await weekly.HandleKeyAsync(chord),
            ItemListViewModel list => await list.HandleKeyAsync(chord),
            _ => false,
        };
    }

    // An open prompt or title editor receives Enter/Escape (and drop-reason digits) even while a text box has focus.
    async Task<bool> RoutePromptKeyAsync(KeyChord chord)
    {
        if (Inspector.Decisions.Prompt is not null)
            return await Inspector.Decisions.HandlePromptKeyAsync(chord);
        return Content switch
        {
            ItemListViewModel { Decisions.Prompt: not null }
            or ItemListViewModel { IsEditingTitle: true } => await (
                (ItemListViewModel)Content
            ).HandleKeyAsync(chord),
            ReviewViewModel { Decisions.Prompt: not null } review => await review.HandleKeyAsync(
                chord
            ),
            ShutdownViewModel { Review.Decisions.Prompt: not null } shutdown =>
                await shutdown.HandleKeyAsync(chord),
            _ => false,
        };
    }

    async Task ExecuteGlobalAsync(Binding binding)
    {
        switch (binding.Action)
        {
            case AppAction.CommandBar:
                await CommandBar.OpenAsync();
                break;
            case AppAction.ToggleInspector:
                ToggleInspector();
                break;
            case AppAction.ToggleSidebar:
                ToggleSidebar();
                break;
            case AppAction.JumpToday:
                await GoAsync(AppPage.Today);
                break;
            case AppAction.WorkspaceN:
                if (binding.Arg - 1 < Workspaces.Count)
                    await SelectWorkspaceAsync(Workspaces[binding.Arg - 1].Id);
                break;
            case AppAction.TodayAll:
                await GoAsync(AppPage.TodayAll);
                break;
            case AppAction.ModeSwitcher:
                await GoAsync(AppPage.Settings);
                break;
            case AppAction.StartReview:
                await StartReviewAsync();
                break;
            case AppAction.Shutdown:
                await GoAsync(AppPage.Shutdown);
                break;
            case AppAction.Undo:
                await UndoAsync();
                break;
            case AppAction.SelectAll when Content is ItemListViewModel list:
                list.SelectAll();
                break;
        }
    }
}
