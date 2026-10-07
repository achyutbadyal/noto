using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Services;
using Noto.Core.Models;
using Noto.Core.Presets;
using Noto.Platform.Abstractions;

namespace Noto.App.ViewModels;

public sealed record CapabilityRow(string Name, bool IsSupported, string? Reason)
{
    public string Text => IsSupported ? "Available" : Reason ?? "Not available";
}

// Per-workspace controls (preset, layout × order × pressure, capacity, day) plus device-level appearance.
public sealed partial class SettingsViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly Guid _workspaceId;
    bool _loading;

    public SettingsViewModel(AppServices services, Guid workspaceId, AppearanceViewModel appearance)
    {
        _services = services;
        _workspaceId = workspaceId;
        Appearance = appearance;
        Capabilities =
        [
            new(
                "Global quick-capture hotkey",
                services.Platform.Hotkey.Capability.IsSupported,
                services.Platform.Hotkey.Capability.Reason
            ),
            new(
                "Secure credential storage",
                services.Platform.Keyring.Capability.IsSupported,
                services.Platform.Keyring.Capability.Reason
            ),
            new(
                "Capture with context",
                services.Platform.CaptureContext.Capability.IsSupported,
                services.Platform.CaptureContext.Capability.Reason
            ),
            new(
                "Notifications",
                services.Platform.Notifications.Capability.IsSupported,
                services.Platform.Notifications.Capability.Reason
            ),
        ];
    }

    public AppearanceViewModel Appearance { get; }
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
            var result = await _services.Import.ImportAsync(_workspaceId, items);
            DataStatus =
                $"Imported {result.Created} items ({result.Completed} completed)"
                + (result.Errors.Count > 0 ? $", {result.Errors.Count} skipped" : "");
            _services.Runner.NotifyChanged();
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

    public Task<string> ExportJsonAsync() => _services.Export.ExportJsonAsync(_workspaceId);

    public Task<string> ExportCsvAsync() => _services.Export.ExportItemsCsvAsync(_workspaceId);

    public IReadOnlyList<CapabilityRow> Capabilities { get; }
    public IReadOnlyList<Preset> Presets => BuiltInPresets.All;
    public IReadOnlyList<Layout> Layouts { get; } = Enum.GetValues<Layout>();
    public IReadOnlyList<SortOrderMode> Orders { get; } = Enum.GetValues<SortOrderMode>();
    public IReadOnlyList<Pressure> Pressures { get; } = Enum.GetValues<Pressure>();
    public IReadOnlyList<CapacityUnit> Units { get; } = Enum.GetValues<CapacityUnit>();
    public IReadOnlyList<ThemeChoice> Themes { get; } = Enum.GetValues<ThemeChoice>();
    public IReadOnlyList<Density> Densities { get; } = Enum.GetValues<Density>();

    [ObservableProperty]
    string _name = "";

    [ObservableProperty]
    Preset? _selectedPreset;

    [ObservableProperty]
    Layout _layout;

    [ObservableProperty]
    SortOrderMode _order;

    [ObservableProperty]
    Pressure _pressure;

    [ObservableProperty]
    CapacityUnit _unit;

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

    public async Task LoadAsync()
    {
        var ws =
            await _services.Workspaces.GetAsync(_workspaceId)
            ?? throw new InvalidOperationException("Workspace not found");
        _loading = true;
        Name = ws.Name;
        Layout = ws.Layout;
        Order = ws.SortOrderMode;
        Pressure = ws.Pressure;
        Unit = ws.CapacityUnit;
        Capacity = ws.DailyCapacity;
        DayBoundary = ws.DayBoundary.ToString("HH:mm");
        FollowsDevice = ws.TzFollowsDevice;
        TimeZone = ws.TimeZone;
        SelectedPreset = BuiltInPresets.Find(ws.Preset.Replace(" (custom)", ""));
        PresetLabel = BuiltInPresets.Label(ws);
        WhatChanges = Describe(ws.Pressure, ws.SortOrderMode);
        _loading = false;
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (!_loading && value is not null)
            _ = SaveAsync(ws => value.ApplyTo(ws));
    }

    partial void OnLayoutChanged(Layout value)
    {
        if (!_loading)
            _ = SaveAsync(ws => ws.Layout = value);
    }

    partial void OnOrderChanged(SortOrderMode value)
    {
        if (!_loading)
            _ = SaveAsync(ws => ws.SortOrderMode = value);
    }

    partial void OnPressureChanged(Pressure value)
    {
        if (!_loading)
            _ = SaveAsync(ws => ws.Pressure = value);
    }

    partial void OnUnitChanged(CapacityUnit value)
    {
        if (!_loading)
            _ = SaveAsync(ws => ws.CapacityUnit = value);
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
        await _services.Workspaces.UpdateAsync(_workspaceId, edit);
        await LoadAsync();
        _services.Runner.NotifyChanged();
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
