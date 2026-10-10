using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Logic;
using Noto.App.Services;
using Noto.App.Themes;
using Noto.Core.Export;
using Noto.Core.Import;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Platform.Abstractions;
using Noto.Providers.Ai;

namespace Noto.App.ViewModels;

public sealed record CapabilityRow(string Name, bool IsSupported, string? Reason)
{
    public string Text => IsSupported ? "Available" : Reason ?? "Not available";
}

// A category in the settings rail. One pane is shown at a time, so the page reads as a settings window
// rather than a 600-line scroll (docs/07 §10.2).
public sealed record SettingsCategory(string Key, string Title);

// One AI mode in the picker. `Reason` is set when the mode cannot run here, so the pane can say why
// instead of offering a dead choice (docs/07 §19: degrade visibly).
public sealed record AiModeChoice(AiMode Mode, string Label, string Description)
{
    public override string ToString() => Label;
}

// One accent swatch. IsSelected is observable so the ring follows the workspace without the view having
// to compare strings.
//
// The swatch deliberately holds no brush: an Avalonia brush is a UI-thread object, and a view-model can
// be built on any thread. The view resolves the colour through RowConverters.Accent at layout time.
public sealed partial class AccentSwatchViewModel(string name, string label) : ObservableObject
{
    public string Name { get; } = name;
    public string Label { get; } = label;

    [ObservableProperty]
    bool _isSelected;
}

// A dropdown entry: the value we store plus the words a person reads. The label is deliberately the
// same phrase the in-app guide uses, so the control and its documentation say the same thing
// (HelpTests asserts the two stay in sync).
public sealed record EnumOption<T>(T Value, string Label)
    where T : struct, Enum
{
    public override string ToString() => Label;
}

// Per-workspace controls (preset, layout × order × pressure, capacity, day) plus device-level appearance.
public sealed partial class SettingsViewModel : ObservableObject
{
    readonly ImportService _import;
    readonly ExportService _export;
    readonly ActionRunner _runner;
    readonly WorkspaceActions _workspaces;
    readonly Guid _workspaceId;
    bool _loading;

    public SettingsViewModel(
        PlatformServices platform,
        ImportService import,
        ExportService export,
        ActionRunner runner,
        WorkspaceActions workspaces,
        Guid workspaceId,
        AppearanceViewModel appearance,
        AccountViewModel? account = null,
        AiOptions? ai = null
    )
    {
        _import = import;
        _export = export;
        _runner = runner;
        _workspaces = workspaces;
        _workspaceId = workspaceId;
        Appearance = appearance;
        Account = account;
        Ai = ai;

        // "Account" only exists when the host has an account service.
        Categories =
        [
            .. new[]
            {
                account is not null ? new SettingsCategory("account", "Account & sync") : null,
                new SettingsCategory("workspace", "Workspace & modes"),
                new SettingsCategory("capacity", "Capacity & day"),
                ai is not null ? new SettingsCategory("ai", "AI suggestions") : null,
                new SettingsCategory("appearance", "Appearance"),
                new SettingsCategory("device", "Data & this device"),
            }.OfType<SettingsCategory>(),
        ];
        _category = Categories[0];
        Capabilities =
        [
            new(
                "Global quick-capture hotkey",
                platform.Hotkey.Capability.IsSupported,
                platform.Hotkey.Capability.Reason
            ),
            new(
                "Secure credential storage",
                platform.Keyring.Capability.IsSupported,
                platform.Keyring.Capability.Reason
            ),
            new(
                "Capture with context",
                platform.CaptureContext.Capability.IsSupported,
                platform.CaptureContext.Capability.Reason
            ),
            new(
                "Notifications",
                platform.Notifications.Capability.IsSupported,
                platform.Notifications.Capability.Reason
            ),
        ];
    }

    public AppearanceViewModel Appearance { get; }

    // Null when the host has no sync server or account service (tests, offline-only builds).
    public AccountViewModel? Account { get; }
    public bool HasAccount => Account is not null;

    // Null when the host has no AI configuration (tests, offline-only builds).
    public AiOptions? Ai { get; }
    public bool HasAi => Ai is not null;

    // The rail. One pane shows at a time; each Show* flag drives exactly one pane's IsVisible.
    public IReadOnlyList<SettingsCategory> Categories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowAccount),
        nameof(ShowWorkspace),
        nameof(ShowCapacity),
        nameof(ShowAi),
        nameof(ShowAppearance),
        nameof(ShowDevice)
    )]
    SettingsCategory _category;

    public bool ShowAccount => HasAccount && Category.Key == "account";
    public bool ShowWorkspace => Category.Key == "workspace";
    public bool ShowCapacity => Category.Key == "capacity";
    public bool ShowAi => HasAi && Category.Key == "ai";
    public bool ShowAppearance => Category.Key == "appearance";
    public bool ShowDevice => Category.Key == "device";
    public IReadOnlyList<Noto.Core.Import.ImportFormat> ImportFormats { get; } =
        Enum.GetValues<Noto.Core.Import.ImportFormat>();

    [ObservableProperty]
    Noto.Core.Import.ImportFormat _importFormat = Noto.Core.Import.ImportFormat.MarkdownChecklist;

    [ObservableProperty]
    string? _dataStatus;

    // Import: imported open items start with carry 0, so nobody is punished for history from another app.
    public async Task ImportAsync(string content)
    {
        try
        {
            var items = Noto.Core.Import.Importers.Parse(ImportFormat, content);
            var result = await _import.ImportAsync(_workspaceId, items);
            DataStatus =
                $"Imported {result.Created} items ({result.Completed} completed)"
                + (result.Errors.Count > 0 ? $", {result.Errors.Count} skipped" : "");
            _runner.NotifyChanged();
        }
        // Parsers surface malformed files as format, JSON or CSV exceptions; the user just needs to know it failed.
        catch (Exception e)
            when (e
                    is Noto.Core.Import.ImportFormatException
                        or System.Text.Json.JsonException
                        or FormatException
                        or InvalidOperationException
                        or CsvHelper.CsvHelperException
            )
        {
            DataStatus = $"Couldn't read that file as {ImportFormat}: {e.Message.Split('\n')[0]}";
        }
    }

    public Task<string> ExportJsonAsync() => _export.ExportJsonAsync(_workspaceId);

    public Task<string> ExportCsvAsync() => _export.ExportItemsCsvAsync(_workspaceId);

    public IReadOnlyList<CapabilityRow> Capabilities { get; }
    public IReadOnlyList<Preset> Presets => BuiltInPresets.All;

    // Friendly labels, not enum names: "PriorityCarry" tells a new user nothing.
    public IReadOnlyList<EnumOption<Layout>> LayoutOptions { get; } =
    [.. Enum.GetValues<Layout>().Select(v => new EnumOption<Layout>(v, ModeLabels.Of(v)))];

    public IReadOnlyList<EnumOption<SortOrderMode>> OrderOptions { get; } =
    [
        .. Enum.GetValues<SortOrderMode>()
            .Select(v => new EnumOption<SortOrderMode>(v, ModeLabels.Of(v))),
    ];

    public IReadOnlyList<EnumOption<Pressure>> PressureOptions { get; } =
    [.. Enum.GetValues<Pressure>().Select(v => new EnumOption<Pressure>(v, ModeLabels.Of(v)))];

    public IReadOnlyList<EnumOption<CapacityUnit>> UnitOptions { get; } =
    [
        .. Enum.GetValues<CapacityUnit>()
            .Select(v => new EnumOption<CapacityUnit>(v, ModeLabels.Of(v))),
    ];

    public IReadOnlyList<ThemeChoice> Themes { get; } = Enum.GetValues<ThemeChoice>();
    public IReadOnlyList<Density> Densities { get; } = Enum.GetValues<Density>();

    // The ten editorial inks. A workspace stores the *name*, so its colour adapts per theme.
    public ObservableCollection<AccentSwatchViewModel> Accents { get; } =
    [
        .. ThemeTokens.Accents.Keys.Select(name => new AccentSwatchViewModel(
            name,
            char.ToUpperInvariant(name[0]) + name[1..]
        )),
    ];

    [ObservableProperty]
    string _customAccent = "";

    // The accent is workspace data, not a device setting: each workspace keeps its own ink.
    [RelayCommand]
    async Task PickAccentAsync(AccentSwatchViewModel choice) =>
        await SaveAsync(ws => ws.Color = choice.Name);

    [RelayCommand]
    async Task ApplyCustomAccentAsync()
    {
        var value = CustomAccent.Trim();
        if (!value.StartsWith('#') || !Color.TryParse(value, out _))
        {
            Error = "Use a hex colour like #C2410C";
            return;
        }
        await SaveAsync(ws => ws.Color = value.ToUpperInvariant());
    }

    [ObservableProperty]
    string _name = "";

    [ObservableProperty]
    Preset? _selectedPreset;

    [ObservableProperty]
    EnumOption<Layout>? _layoutChoice;

    [ObservableProperty]
    EnumOption<SortOrderMode>? _orderChoice;

    [ObservableProperty]
    EnumOption<Pressure>? _pressureChoice;

    [ObservableProperty]
    EnumOption<CapacityUnit>? _unitChoice;

    [ObservableProperty]
    int _capacity;

    [ObservableProperty]
    string _dayBoundary = "00:00";

    [ObservableProperty]
    bool _followsDevice;

    [ObservableProperty]
    string _timeZone = "";

    [ObservableProperty]
    string _presetLabel = "";

    [ObservableProperty]
    string _whatChanges = "";

    [ObservableProperty]
    string? _error;

    // ---- AI suggestions ----
    //
    // "Off" is the hide-everything switch: the sparkle disappears from the create form and nothing is
    // ever sent anywhere. The other modes fill that same form from a model the user chooses and pays for.

    public IReadOnlyList<AiModeChoice> AiModes { get; } =
    [
        .. Enum.GetValues<AiMode>()
            .Select(m => new AiModeChoice(m, AiOptions.Label(m), AiOptions.Description(m))),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiNeedsEndpoint), nameof(AiNeedsKey))]
    AiModeChoice? _aiChoice;

    [ObservableProperty]
    string _aiEndpoint = "";

    [ObservableProperty]
    string _aiModel = "";

    // Write-only: the stored key is never read back into the UI, only replaced.
    [ObservableProperty]
    string _aiKey = "";

    [ObservableProperty]
    string? _aiStatus;

    public bool AiHasKey => Ai?.HasKey == true;

    public bool AiNeedsEndpoint => AiChoice?.Mode is AiMode.Local or AiMode.Cloud;

    public bool AiNeedsKey => AiChoice?.Mode is AiMode.Cloud;

    public string AiKeyHint =>
        AiHasKey
            ? "A key is stored. Type a new one to replace it."
            : "Stored in the system keyring, never in the database.";

    // Why the chosen mode can't run here, if it can't.
    public string? AiModeReason => Ai?.Registry.ReasonUnavailable(AiChoice?.Mode ?? AiMode.Off);

    partial void OnAiChoiceChanged(AiModeChoice? value)
    {
        if (
            !_loading
            && value is not null
            && AiEndpoint.Trim().Length == 0
            && value.Mode is AiMode.Local or AiMode.Cloud
        )
            AiEndpoint = AiOptions.DefaultEndpoint(value.Mode);
        OnPropertyChanged(nameof(AiModeReason));
    }

    [RelayCommand]
    public async Task SaveAiAsync()
    {
        if (Ai is null)
            return;
        await Ai.SaveAsync(AiChoice?.Mode ?? AiMode.Off, AiEndpoint, AiModel, AiKey);
        AiKey = "";
        AiStatus =
            AiChoice?.Mode == AiMode.Off ? "AI is off. The sparkle is hidden everywhere."
            : Ai.IsEnabled ? $"{AiOptions.Label(Ai.Mode)} is on."
            : "Saved, but this mode can't run yet — see the note above.";
        OnPropertyChanged(nameof(AiHasKey));
        OnPropertyChanged(nameof(AiKeyHint));
        _runner.NotifyChanged();
    }

    // The helper is a file on disk, so it can appear (or be removed) while the app is running. Re-read
    // it here instead of making the user restart the app to clear a stale "not installed" note.
    [RelayCommand]
    public void RecheckAi()
    {
        OnPropertyChanged(nameof(AiModeReason));
        OnPropertyChanged(nameof(AiHasKey));
        OnPropertyChanged(nameof(AiKeyHint));
        AiStatus = AiModeReason is null && AiChoice?.Mode != AiMode.Off ? "Ready to use." : null;
    }

    public async Task LoadAsync()
    {
        var ws =
            await _workspaces.GetAsync(_workspaceId)
            ?? throw new InvalidOperationException("Workspace not found");
        _loading = true;
        Name = ws.Name;
        LayoutChoice = LayoutOptions.First(o => o.Value == ws.Layout);
        OrderChoice = OrderOptions.First(o => o.Value == ws.SortOrderMode);
        PressureChoice = PressureOptions.First(o => o.Value == ws.Pressure);
        UnitChoice = UnitOptions.First(o => o.Value == ws.CapacityUnit);
        Capacity = ws.DailyCapacity;
        DayBoundary = ws.DayBoundary.ToString("HH:mm");
        FollowsDevice = ws.TzFollowsDevice;
        TimeZone = ws.TimeZone;
        SelectedPreset = BuiltInPresets.Find(ws.Preset.Replace(" (custom)", ""));
        PresetLabel = BuiltInPresets.Label(ws);
        WhatChanges = Describe(ws.Pressure, ws.SortOrderMode);

        // AccentKey normalises a name or a literal #rrggbb, so an unknown name falls back to the default
        // and a literal colour simply matches no swatch.
        var accent = ThemeTokens.AccentKey(ws.Color);
        foreach (var swatch in Accents)
            swatch.IsSelected = swatch.Name == accent;
        CustomAccent = ws.Color?.StartsWith('#') == true ? ws.Color : "";

        if (Ai is not null)
        {
            AiChoice = AiModes.FirstOrDefault(c => c.Mode == Ai.Mode) ?? AiModes[0];
            AiEndpoint =
                Ai.Endpoint.Length > 0 ? Ai.Endpoint
                : Ai.Mode is AiMode.Local or AiMode.Cloud ? AiOptions.DefaultEndpoint(Ai.Mode)
                : "";
            AiModel = Ai.Model;
            AiKey = "";
            AiStatus = null;
            // AiChoice only raises when the value actually changes, so opening this pane again after
            // installing the helper would otherwise keep showing the stale "not installed" note.
            OnPropertyChanged(nameof(AiModeReason));
            OnPropertyChanged(nameof(AiKeyHint));
        }

        _loading = false;
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ws => value.ApplyTo(ws));
    }

    partial void OnLayoutChoiceChanged(EnumOption<Layout>? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ws => ws.Layout = value.Value);
    }

    partial void OnOrderChoiceChanged(EnumOption<SortOrderMode>? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ws => ws.SortOrderMode = value.Value);
    }

    partial void OnPressureChoiceChanged(EnumOption<Pressure>? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ws => ws.Pressure = value.Value);
    }

    partial void OnUnitChoiceChanged(EnumOption<CapacityUnit>? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ws => ws.CapacityUnit = value.Value);
    }

    partial void OnCapacityChanged(int value)
    {
        if (!_loading && value > 0)
            _ = SaveAsync(ws => ws.DailyCapacity = value);
    }

    partial void OnFollowsDeviceChanged(bool value)
    {
        if (!_loading)
            _ = SaveAsync(ws => ws.TzFollowsDevice = value);
    }

    [RelayCommand]
    public Task RenameAsync() =>
        Name.Trim().Length == 0 ? Task.CompletedTask : SaveAsync(ws => ws.Name = Name.Trim());

    [RelayCommand]
    public async Task ApplyDayBoundaryAsync()
    {
        if (!TimeOnly.TryParseExact(DayBoundary, "HH:mm", out var boundary))
        {
            Error = "Use a 24-hour time like 04:00";
            return;
        }
        await SaveAsync(ws => ws.DayBoundary = boundary);
    }

    [RelayCommand]
    public async Task ApplyTimeZoneAsync()
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            Error = "Unknown time zone. Use an IANA id like Europe/Berlin.";
            return;
        }
        await SaveAsync(ws => ws.TimeZone = TimeZone);
    }

    async Task SaveAsync(Action<Workspace> edit)
    {
        Error = null;
        await _workspaces.UpdateAsync(_workspaceId, edit);
        await LoadAsync();
        _runner.NotifyChanged();
    }

    // The one-line "what changes" preview shown in the mode switcher (docs/07 §9).
    public static string Describe(Pressure pressure, SortOrderMode order)
    {
        var sort = order switch
        {
            SortOrderMode.PriorityCarry => "sorts by priority, then carry",
            SortOrderMode.CarryDesc => "sorts by carry",
            SortOrderMode.DueDate => "sorts by due date, overdue first",
            SortOrderMode.TimeOfDay => "sorts by time of day",
            _ => "keeps your manual order",
        };
        var push = pressure switch
        {
            Pressure.Gentle => "Morning Review is optional and rows stay calm",
            Pressure.Honest => "Morning Review is on",
            _ => "pins your 3 most-carried items and the Morning Review can't be skipped",
        };
        return $"{char.ToUpper(sort[0])}{sort[1..]}. {push}.";
    }
}
