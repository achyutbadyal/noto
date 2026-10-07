using CommunityToolkit.Mvvm.ComponentModel;

namespace Noto.App.ViewModels;

public sealed partial class WorkspaceTabViewModel(Guid id, string name, string icon, string color, int index) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public string Icon { get; } = icon;
    public string Color { get; } = color;
    public int Index { get; } = index;
    public string Shortcut => Index < 9 ? $"⌘{Index + 1}" : "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBadge), nameof(BadgeText), nameof(AutomationName))] int _needsDecision;
    [ObservableProperty] bool _isSelected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBadge), nameof(AutomationName))] bool _isQuiet;

    // Only items needing a decision are counted, not open items; outside focus hours the badge goes quiet.
    public bool HasBadge => NeedsDecision > 0 && !IsQuiet;
    public string BadgeText => NeedsDecision.ToString();
    public string AutomationName => HasBadge ? $"{Name}, {NeedsDecision} need a decision" : Name;
}
