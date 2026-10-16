using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.App.Views;
using Noto.Platform;
using Noto.Platform.Abstractions;

namespace Noto.App.Tests.Ui;

// The Credentials pane in Settings (docs/05 › ADR-06). It is the one place in the app that can still touch
// the OS keychain, so what it offers, when it offers it, and whether the result is announced are pinned
// here rather than left to the view-model tests.
public sealed class SecretStoragePaneTests : IDisposable
{
    readonly AppFixture _app = new();
    readonly string _dir = Directory.CreateTempSubdirectory("noto-pane").FullName;

    public void Dispose()
    {
        _app.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    SettingsViewModel Build(
        bool hostCanNameSecrets,
        SecretStorageMode mode = SecretStorageMode.MachineEncrypted
    )
    {
        FileKeyring.WriteMode(_dir, mode);
        var store = new FileKeyring(
            _dir,
            Guid.NewGuid().ToString(),
            new FakeOsKeyring(),
            Path.Combine(_dir, ".keyring.key")
        );
        var platform = new PlatformServices(
            _app.Services.Platform.Hotkey,
            store,
            _app.Services.Platform.CaptureContext,
            _app.Services.Platform.Notifications,
            _app.Services.Platform.ReduceMotion,
            store
        );
        return new SettingsViewModel(
            platform,
            _app.Services.Import,
            _app.Services.Export,
            _app.Services.Runner,
            _app.Services.Workspaces,
            _app.Workspace.Id,
            new AppearanceViewModel(_app.Services.UiState),
            secrets: store,
            knownSecrets: hostCanNameSecrets
                ? () =>
                    Task.FromResult<IReadOnlyList<(string Service, string Account)>>([
                        ("app.noto.connections", "conn-1"),
                    ])
                : null
        );
    }

    // The pane is only on screen in the device category, and its Credentials section only when the host has
    // a file-backed store.
    async Task<(SettingsView View, SettingsViewModel Vm)> ShowAsync(SettingsViewModel vm)
    {
        vm.Category = vm.Categories.First(c => c.Key == "device");
        await vm.LoadAsync();

        var view = new SettingsView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 400,
            Height = 700,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, vm);
    }

    static Button ButtonWith(Visual root, string content) =>
        root.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == content);

    [AvaloniaFact]
    public async Task The_clean_up_action_is_offered_only_when_the_host_can_name_every_secret()
    {
        // Effectively visible, not IsVisible: the button's own flag stays true under a hidden section, so
        // only the ancestor-aware one answers "would the user see this".
        var (without, _) = await ShowAsync(Build(hostCanNameSecrets: false));
        ButtonWith(without, "Clean up the system keychain").IsEffectivelyVisible.ShouldBeFalse();

        var (with, _) = await ShowAsync(Build(hostCanNameSecrets: true));
        ButtonWith(with, "Clean up the system keychain").IsEffectivelyVisible.ShouldBeTrue();
    }

    // Both results are text that appears after the fact: without a live setting a screen reader never hears
    // that the store changed or that the keychain was cleaned.
    [AvaloniaFact]
    public async Task Both_results_are_announced()
    {
        var (view, vm) = await ShowAsync(Build(hostCanNameSecrets: true));

        vm.SecretStatus = "Secrets are now stored as plain file.";
        vm.KeyringStatus = "Nothing of Noto's is left in the system keychain.";
        Dispatcher.UIThread.RunJobs();

        var announced = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t =>
                t.IsVisible
                && AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite
            )
            .Select(t => t.Text)
            .ToList();

        announced.ShouldBe([vm.SecretStatus, vm.KeyringStatus], ignoreOrder: true);
    }

    // A hint in a horizontal StackPanel is measured unbounded, so its TextWrapping can never engage and a
    // long line clips instead of wrapping. Narrow on purpose: at this width the hint has to wrap.
    [AvaloniaFact]
    public async Task The_hint_wraps_instead_of_overflowing_the_pane()
    {
        var (view, vm) = await ShowAsync(Build(hostCanNameSecrets: true));

        var hint = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .First(t => t.Text == vm.SecretHint);
        var parent = (Visual)hint.Parent!;

        hint.Bounds.Right.ShouldBeLessThanOrEqualTo(parent.Bounds.Width + 0.5);
    }
}
