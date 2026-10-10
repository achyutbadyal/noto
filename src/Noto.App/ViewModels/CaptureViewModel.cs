using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.Core.Ai;
using Noto.Core.Links;
using Noto.Core.Time;
using Noto.Core.Workspaces;
using Noto.Platform.Abstractions;

namespace Noto.App.ViewModels;

public sealed record WorkspaceChoice(Guid Id, string Name, string Icon);

// The global quick-capture panel (docs/07 §7.2): token input, a workspace picker that defaults by focus hours,
// and an "attach current page" toggle when the frontmost app is a browser.
public sealed partial class CaptureViewModel : ObservableObject
{
    readonly IClock _clock;
    readonly PlatformServices _platform;
    readonly WorkspaceActions _workspaces;
    readonly LinkIndexer _links;
    readonly ActionRunner _runner;
    readonly ISuggestionService _suggestions;
    readonly ILinkResolver? _linkResolver;
    IReadOnlyList<WorkspaceChoice> _all = [];

    public CaptureViewModel(
        IClock clock,
        PlatformServices platform,
        WorkspaceActions workspaces,
        LinkIndexer links,
        ActionRunner runner,
        ISuggestionService? suggestions = null,
        ILinkResolver? linkResolver = null
    )
    {
        _clock = clock;
        _platform = platform;
        _workspaces = workspaces;
        _links = links;
        _runner = runner;
        _suggestions = suggestions ?? NullSuggestionService.Instance;
        _linkResolver = linkResolver;
        Add = new AddItemViewModel(
            runner,
            Guid.Empty,
            plannedForToday: true,
            null,
            () => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            _suggestions,
            _linkResolver
        );
    }

    public ObservableCollection<WorkspaceChoice> Workspaces { get; } = [];
    public AddItemViewModel Add { get; private set; }

    [ObservableProperty]
    WorkspaceChoice? _selected;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAttachOffer), nameof(AttachText))]
    CaptureContext? _context;

    [ObservableProperty]
    bool _attachPage;

    [ObservableProperty]
    string? _status;

    public bool HasAttachOffer => Context?.Url is not null;
    public string AttachText =>
        Context?.Url is null ? "" : $"Attach current page  ⌘L   {Context.PageTitle}";

    public event Action? Saved;
    public event Action? CloseRequested;

    // Called each time the panel is shown.
    public async Task PrepareAsync()
    {
        var workspaces = await _workspaces.ListAsync();
        _all = workspaces.Select(w => new WorkspaceChoice(w.Id, w.Name, w.Icon)).ToList();
        Workspaces.Clear();
        foreach (var w in _all)
            Workspaces.Add(w);

        // Default to a workspace that is inside its focus hours, so Work stays quiet in the evening.
        var active =
            workspaces.FirstOrDefault(w => FocusHours.IsActive(w, _clock))
            ?? workspaces.FirstOrDefault();
        Select(active is null ? null : Workspaces.First(w => w.Id == active.Id));

        Status = null;
        AttachPage = false;
        Context = _platform.CaptureContext.Capability.IsSupported
            ? await _platform.CaptureContext.GetAsync()
            : null;
    }

    public void Select(WorkspaceChoice? choice)
    {
        Selected = choice;
        var text = Add.Text;
        Add = new AddItemViewModel(
            _runner,
            choice?.Id ?? Guid.Empty,
            plannedForToday: true,
            name =>
                _all.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    is { } w
                    ? w.Id
                    : null,
            () => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            _suggestions,
            _linkResolver
        )
        {
            Text = text,
        };
        OnPropertyChanged(nameof(Add));
    }

    public void ToggleAttach()
    {
        if (HasAttachOffer)
            AttachPage = !AttachPage;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Selected is null)
        {
            Status = "Create a workspace first";
            return;
        }
        if (!await Add.SubmitAsync())
        {
            Status = Add.Error;
            return;
        }

        if (AttachPage && Context?.Url is { } url && Add.LastCreatedId is { } id)
        {
            try
            {
                await _links.AddExplicitAsync(id, url);
            }
            catch (ArgumentException)
            { /* non-http pages are simply not attached */
            }
        }
        Saved?.Invoke();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void Close() => CloseRequested?.Invoke();

    public async Task<bool> HandleKeyAsync(KeyChord chord)
    {
        switch (chord)
        {
            case { Key: "Escape" }:
                Close();
                return true;
            case { Key: "Enter" }:
                await SaveAsync();
                return true;
            case { Key: "l", Command: true }:
                ToggleAttach();
                return true;
            case { Key: "Tab", Shift: false } when Workspaces.Count > 1:
                Select(Workspaces[(Workspaces.IndexOf(Selected!) + 1) % Workspaces.Count]);
                return true;
            default:
                return false;
        }
    }
}
