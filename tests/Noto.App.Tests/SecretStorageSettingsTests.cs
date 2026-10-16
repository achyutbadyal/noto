using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Platform;
using Noto.Platform.Abstractions;

namespace Noto.App.Tests;

// The credential picker in Settings. The rest of the app builds this store in Composition; here it is built
// over a scratch directory so switching a mode can be checked end to end (docs/05 › ADR-06).
public sealed class SecretStorageSettingsTests : IDisposable
{
    readonly AppFixture _app = new();
    readonly string _dir = Directory.CreateTempSubdirectory("noto-settings").FullName;

    public void Dispose()
    {
        _app.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    SettingsViewModel Build(SecretStorageMode mode)
    {
        FileKeyring.WriteMode(_dir, mode);
        var store = new FileKeyring(_dir, Guid.NewGuid().ToString(), keyFilePath: KeyFile);
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
            secrets: store
        );
    }

    string KeyFile => Path.Combine(_dir, ".keyring.key");

    string StoreFile(string name) => Path.Combine(_dir, name);

    [Fact]
    public async Task The_picker_offers_every_mode_and_starts_on_the_active_one()
    {
        var vm = Build(SecretStorageMode.PlainFile);
        await vm.LoadAsync();

        vm.HasSecrets.ShouldBeTrue();
        vm.SecretModes.Select(m => m.Mode).ShouldBe(Enum.GetValues<SecretStorageMode>());
        vm.SecretModes.ShouldAllBe(m => m.Label.Length > 0 && m.Description.Length > 0);
        vm.SecretChoice!.Mode.ShouldBe(SecretStorageMode.PlainFile);
        vm.SecretNeedsPassphrase.ShouldBeFalse();
        vm.SecretActionLabel.ShouldBe("Apply");
    }

    [Fact]
    public async Task Switching_from_the_picker_re_keys_the_secrets()
    {
        var vm = Build(SecretStorageMode.PlainFile);
        await vm.LoadAsync();
        await vm.Secrets!.SetAsync("app.noto.ai", "api-key", "sk-test");

        vm.SecretChoice = vm.SecretModes.First(m => m.Mode == SecretStorageMode.MachineEncrypted);
        await vm.ApplySecretStorageAsync();

        vm.Secrets.Mode.ShouldBe(SecretStorageMode.MachineEncrypted);
        FileKeyring.ReadMode(_dir).ShouldBe(SecretStorageMode.MachineEncrypted);
        (await vm.Secrets.GetAsync("app.noto.ai", "api-key")).ShouldBe("sk-test");
        File.Exists(StoreFile("secrets.json")).ShouldBeFalse();
        vm.SecretStatus.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_passphrase_mode_needs_a_passphrase_and_then_reports_itself_unlocked()
    {
        var vm = Build(SecretStorageMode.MachineEncrypted);
        await vm.LoadAsync();
        await vm.Secrets!.SetAsync("app.noto.server", "session", "refresh-token");

        vm.SecretChoice = vm.SecretModes.First(m =>
            m.Mode == SecretStorageMode.PassphraseEncrypted
        );
        vm.SecretNeedsPassphrase.ShouldBeTrue();

        // Nothing typed yet: the pane explains instead of half-switching the store.
        await vm.ApplySecretStorageAsync();
        vm.SecretStatus.ShouldNotBeNull();
        vm.Secrets.Mode.ShouldBe(SecretStorageMode.MachineEncrypted);

        vm.SecretPassphrase = "hunter2";
        await vm.ApplySecretStorageAsync();

        vm.Secrets.Mode.ShouldBe(SecretStorageMode.PassphraseEncrypted);
        vm.SecretLocked.ShouldBeFalse();
        vm.SecretPassphrase.ShouldBeEmpty(); // never kept in the form
        (await vm.Secrets.GetAsync("app.noto.server", "session")).ShouldBe("refresh-token");
    }

    [Fact]
    public async Task A_host_without_a_file_store_offers_no_picker()
    {
        var vm = _app.Vms.Settings(
            _app.Workspace.Id,
            new AppearanceViewModel(_app.Services.UiState),
            null
        );
        await vm.LoadAsync();

        vm.HasSecrets.ShouldBeFalse();
        vm.Secrets.ShouldBeNull();
    }
}
