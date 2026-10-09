using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Services;
using Noto.Core.Presets;

namespace Noto.App.ViewModels;

public enum OnboardingChoice
{
    Work,
    Personal,
    Both,
}

// First launch asks one question: "What do you want Noto to keep honest?" (docs/07 §16).
public sealed partial class OnboardingViewModel(WorkspaceActions workspaces) : ObservableObject
{
    public string Question => "What do you want Noto to keep honest?";
    public IReadOnlyList<OnboardingChoice> Choices { get; } = Enum.GetValues<OnboardingChoice>();

    public event Action? Completed;

    [RelayCommand]
    async Task PickAsync(string choice) => await ChooseAsync(Enum.Parse<OnboardingChoice>(choice));

    public async Task ChooseAsync(OnboardingChoice choice)
    {
        if (choice is OnboardingChoice.Work or OnboardingChoice.Both)
            await workspaces.CreateAsync("Work", "work", BuiltInPresets.Sprint, 0);
        if (choice is OnboardingChoice.Personal or OnboardingChoice.Both)
            await workspaces.CreateAsync("Personal", "personal", BuiltInPresets.Zen, 1);
        Completed?.Invoke();
    }
}
