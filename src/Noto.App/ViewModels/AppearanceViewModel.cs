using CommunityToolkit.Mvvm.ComponentModel;
using Noto.App.Services;
using Noto.Core.Presets;

namespace Noto.App.ViewModels;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

// Theme, density and body size are per-device view settings, kept in IUiState.
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

    [ObservableProperty]
    ThemeChoice _theme;

    [ObservableProperty]
    Density _density;

    [ObservableProperty]
    int _bodySize;

    public event Action? Changed;

    public double RowHeight =>
        Density switch
        {
            Density.Compact => 28,
            Density.Spacious => 52,
            _ => 36,
        };

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
