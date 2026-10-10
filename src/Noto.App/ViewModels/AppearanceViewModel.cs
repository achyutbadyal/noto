using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Services;
using Noto.Core.Presets;

namespace Noto.App.ViewModels;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

// Theme, density and body size are per-device view settings, kept in IUiState. The host applies them
// (MainWindow.ApplyAppearance) — this class only owns the values and the words for them.
public sealed partial class AppearanceViewModel : ObservableObject
{
    public const int MinBodySize = 13,
        MaxBodySize = 18;

    public static IReadOnlyList<ThemeChoice> ThemeChoices { get; } = Enum.GetValues<ThemeChoice>();
    public static IReadOnlyList<Density> DensityChoices { get; } = Enum.GetValues<Density>();
    readonly IUiState _state;

    public AppearanceViewModel(IUiState state)
    {
        _state = state;
        _theme = Enum.TryParse<ThemeChoice>(state.Get("theme"), out var t) ? t : ThemeChoice.System;
        _density = Enum.TryParse<Density>(state.Get("density"), out var d)
            ? d
            : Density.Comfortable;
        _bodySize = int.TryParse(state.Get("body-size"), out var s)
            ? Math.Clamp(s, MinBodySize, MaxBodySize)
            : 14;
    }

    [
        ObservableProperty,
        NotifyPropertyChangedFor(
            nameof(ThemeLabel),
            nameof(ThemeDescription),
            nameof(IsSystemTheme),
            nameof(IsLightTheme),
            nameof(IsDarkTheme)
        )
    ]
    ThemeChoice _theme;

    [ObservableProperty]
    Density _density;

    [ObservableProperty]
    int _bodySize;

    // Raised after any change so the host can re-apply theme, density and text size.
    public event Action? Changed;

    public double RowHeight =>
        Density switch
        {
            Density.Compact => 28,
            Density.Spacious => 56,
            _ => 38,
        };

    public string ThemeLabel => ThemeLabelFor(Theme);

    public string ThemeDescription =>
        Theme switch
        {
            ThemeChoice.Light => "Always light",
            ThemeChoice.Dark => "Always dark",
            _ => "Follow this Mac",
        };

    // Drives the segmented control in Settings.
    public bool IsSystemTheme => Theme == ThemeChoice.System;
    public bool IsLightTheme => Theme == ThemeChoice.Light;
    public bool IsDarkTheme => Theme == ThemeChoice.Dark;

    // A stable, human label — used by the segmented control, the command bar and the guide.
    public static string ThemeLabelFor(ThemeChoice choice) =>
        choice switch
        {
            ThemeChoice.Light => "Light",
            ThemeChoice.Dark => "Dark",
            _ => "System",
        };

    // ⌘⇧L and the command bar: System -> Light -> Dark -> System.
    public void CycleTheme() =>
        SetTheme(
            Theme switch
            {
                ThemeChoice.System => ThemeChoice.Light,
                ThemeChoice.Light => ThemeChoice.Dark,
                _ => ThemeChoice.System,
            }
        );

    [RelayCommand]
    public void SetTheme(ThemeChoice choice) => Theme = choice;

    public void SetDensity(Density density) => Density = density;

    partial void OnThemeChanged(ThemeChoice value) => Save("theme", value.ToString());

    partial void OnDensityChanged(Density value)
    {
        Save("density", value.ToString());
        OnPropertyChanged(nameof(RowHeight));
    }

    partial void OnBodySizeChanged(int value) =>
        Save("body-size", Math.Clamp(value, MinBodySize, MaxBodySize).ToString());

    void Save(string key, string value)
    {
        _state.Set(key, value);
        Changed?.Invoke();
    }
}
