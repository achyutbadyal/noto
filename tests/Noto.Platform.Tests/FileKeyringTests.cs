using System.Text;
using System.Text.Json;
using Noto.Platform;
using Noto.Platform.Abstractions;

namespace Noto.Platform.Tests;

// The device secret store. Every test runs against a scratch directory; the keyfile is kept there too so a
// developer's real ~/.config/noto/.keyring.key is never touched.
public sealed class FileKeyringTests : IDisposable
{
    readonly TempDirectory _dir = new();
    readonly string _keyFile;
    readonly string _deviceId = Guid.NewGuid().ToString();

    public FileKeyringTests() => _keyFile = _dir.File(Path.Combine("keys", ".keyring.key"));

    public void Dispose() => _dir.Dispose();

    // Opens a store, first pinning the mode the way the app does (through storage-mode.json).
    FileKeyring Open(SecretStorageMode mode, IKeyring? os = null)
    {
        FileKeyring.WriteMode(_dir.Path, mode);
        return new FileKeyring(_dir.Path, _deviceId, os, _keyFile);
    }

    // A stand-in for the OS keychain that reports itself as *supported*, unlike InMemoryKeyring, so the
    // migration path is actually exercised.
    sealed class FakeOsKeyring : IKeyring
    {
        readonly Dictionary<(string, string), string> _secrets = [];

        public Capability Capability => Capability.Supported;

        public Task SetAsync(string service, string account, string secret)
        {
            _secrets[(service, account)] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string service, string account) =>
            Task.FromResult(
                _secrets.TryGetValue((service, account), out var secret) ? secret : null
            );

        public Task DeleteAsync(string service, string account)
        {
            _secrets.Remove((service, account));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void The_default_is_machine_encrypted_and_a_broken_config_falls_back_to_it()
    {
        FileKeyring.ReadMode(_dir.Path).ShouldBe(SecretStorageMode.MachineEncrypted);

        File.WriteAllText(_dir.File("storage-mode.json"), "{ this is not json");
        FileKeyring.ReadMode(_dir.Path).ShouldBe(SecretStorageMode.MachineEncrypted);

        File.WriteAllText(_dir.File("storage-mode.json"), """{"mode":"SomethingElse"}""");
        FileKeyring.ReadMode(_dir.Path).ShouldBe(SecretStorageMode.MachineEncrypted);
    }

    [Theory]
    [InlineData(SecretStorageMode.MachineEncrypted)]
    [InlineData(SecretStorageMode.PassphraseEncrypted)]
    [InlineData(SecretStorageMode.PlainFile)]
    public async Task Every_file_mode_round_trips_a_secret(SecretStorageMode mode)
    {
        var keyring = Open(mode);
        if (mode == SecretStorageMode.PassphraseEncrypted)
            await keyring.UnlockAsync("correct horse battery staple");

        await keyring.SetAsync("app.noto.ai", "api-key", "sk-live-✓");
        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBe("sk-live-✓");
        (await keyring.GetAsync("app.noto.ai", "no-such-account")).ShouldBeNull();

        // A second store over the same directory sees the same secrets.
        var reopened = Open(mode);
        if (mode == SecretStorageMode.PassphraseEncrypted)
            await reopened.UnlockAsync("correct horse battery staple");
        (await reopened.GetAsync("app.noto.ai", "api-key")).ShouldBe("sk-live-✓");

        await keyring.DeleteAsync("app.noto.ai", "api-key");
        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();
    }

    [Fact]
    public async Task Machine_encrypted_secrets_are_unreadable_on_disk()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);
        await keyring.SetAsync("app.noto.ai", "api-key", "s3cr3t-token");

        var blob = await File.ReadAllBytesAsync(_dir.File("secrets.enc"));
        Encoding.UTF8.GetString(blob).ShouldNotContain("s3cr3t-token");
        File.Exists(_dir.File("secrets.json")).ShouldBeFalse();
        File.Exists(_dir.File("secrets.vault")).ShouldBeFalse();

        // The keyfile is a separate 32 bytes, and it is what makes the file this machine's.
        File.Exists(_keyFile).ShouldBeTrue();
        new FileInfo(_keyFile).Length.ShouldBe(32);
    }

    [Fact]
    public async Task Plain_file_mode_writes_readable_json()
    {
        var keyring = Open(SecretStorageMode.PlainFile);
        await keyring.SetAsync("app.noto.ai", "api-key", "s3cr3t-token");

        (await File.ReadAllTextAsync(_dir.File("secrets.json"))).ShouldContain("s3cr3t-token");
    }

    // The three families the app stores — the sync session, one entry per provider connection, and the AI
    // key — are three (service, account) pairs, but in the three file modes they share ONE JSON object in
    // ONE file. The pairs themselves are the names the app has always used, so moving between modes never
    // has to rename anything.
    //
    // This pins the on-disk shape on purpose: it is a persisted format, and changing it without a
    // migration would strand everyone's secrets.
    [Fact]
    public async Task Every_key_family_shares_one_json_object()
    {
        const string connectionId = "0e5b1f2c-9a4d-4f6e-8b7a-3c2d1e0f9a8b";

        var keyring = Open(SecretStorageMode.PlainFile);
        await keyring.SetAsync("app.noto.server", "session", "refresh-token");
        await keyring.SetAsync("app.noto.connections", connectionId, "provider-token");
        await keyring.SetAsync("app.noto.ai", "api-key", "sk-live");

        var store = JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(_dir.File("secrets.json"))
        )!;

        store.Count.ShouldBe(3);
        store[$"app.noto.server\u0001session"].ShouldBe("refresh-token");
        store[$"app.noto.connections\u0001{connectionId}"].ShouldBe("provider-token");
        store[$"app.noto.ai\u0001api-key"].ShouldBe("sk-live");

        // One file for all of them: there is no per-key file and no per-key flag.
        Directory.GetFiles(_dir.Path, "secrets.*").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_different_device_id_cannot_open_the_store()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);
        await keyring.SetAsync("app.noto.ai", "api-key", "s3cr3t-token");

        // Same directory, same keyfile, different device id: the derived key differs, so the file is an
        // empty store rather than someone else's secrets.
        var stranger = new FileKeyring(_dir.Path, Guid.NewGuid().ToString(), keyFilePath: _keyFile);
        (await stranger.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();
    }

    [Fact]
    public async Task A_wrong_passphrase_is_a_clear_error_that_leaves_the_vault_locked()
    {
        var keyring = Open(SecretStorageMode.PassphraseEncrypted);
        await keyring.UnlockAsync("right");
        await keyring.SetAsync("app.noto.ai", "api-key", "token");

        var reopened = Open(SecretStorageMode.PassphraseEncrypted);
        reopened.IsUnlocked.ShouldBeFalse();
        // A vault nobody has opened yet reads as absent, so startup never breaks on the missing passphrase.
        (await reopened.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();

        var error = await Should.ThrowAsync<SecretStorageException>(() =>
            reopened.UnlockAsync("wrong")
        );
        error.Message.ShouldContain("passphrase");
        reopened.IsUnlocked.ShouldBeFalse();

        await reopened.UnlockAsync("right");
        reopened.IsUnlocked.ShouldBeTrue();
        (await reopened.GetAsync("app.noto.ai", "api-key")).ShouldBe("token");
    }

    [Fact]
    public async Task Writing_to_a_locked_vault_asks_for_the_passphrase()
    {
        var keyring = Open(SecretStorageMode.PassphraseEncrypted);

        var error = await Should.ThrowAsync<SecretStorageException>(() =>
            keyring.SetAsync("app.noto.ai", "api-key", "token")
        );
        error.Message.ShouldContain("passphrase");
        File.Exists(_dir.File("secrets.vault")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(SecretStorageMode.MachineEncrypted, "secrets.enc")]
    [InlineData(SecretStorageMode.PlainFile, "secrets.json")]
    public async Task A_corrupt_file_reads_as_an_empty_store_instead_of_crashing(
        SecretStorageMode mode,
        string fileName
    )
    {
        var keyring = Open(mode);
        await keyring.SetAsync("app.noto.ai", "api-key", "token");
        await File.WriteAllTextAsync(
            _dir.File(fileName),
            "this is not the store you are looking for"
        );

        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();

        // And it heals: the next write replaces the damaged file.
        await keyring.SetAsync("app.noto.ai", "api-key", "fresh");
        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBe("fresh");
    }

    [Fact]
    public async Task Concurrent_writes_all_survive()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);

        await Task.WhenAll(
            Enumerable
                .Range(0, 25)
                .Select(i => keyring.SetAsync("app.noto.connections", $"conn-{i}", $"secret-{i}"))
        );

        for (var i = 0; i < 25; i++)
            (await keyring.GetAsync("app.noto.connections", $"conn-{i}")).ShouldBe($"secret-{i}");
    }

    [Fact]
    public async Task A_key_only_the_os_keyring_has_is_migrated_on_first_read()
    {
        var os = new FakeOsKeyring();
        await os.SetAsync("app.noto.ai", "api-key", "from-keychain");

        var keyring = Open(SecretStorageMode.MachineEncrypted, os);
        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBe("from-keychain");

        // Copied into the encrypted file and taken out of the keychain, so the prompt stops coming back.
        (await os.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();
        var reopened = Open(SecretStorageMode.MachineEncrypted, os);
        (await reopened.GetAsync("app.noto.ai", "api-key")).ShouldBe("from-keychain");
        (await os.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();
    }

    [Fact]
    public async Task Switching_modes_copies_every_key_and_erases_the_old_store()
    {
        var keyring = Open(SecretStorageMode.PlainFile);
        await keyring.SetAsync("app.noto.server", "session", "one");
        await keyring.SetAsync("app.noto.ai", "api-key", "two");
        File.Exists(_dir.File("secrets.json")).ShouldBeTrue();

        await keyring.SwitchModeAsync(SecretStorageMode.MachineEncrypted);

        keyring.Mode.ShouldBe(SecretStorageMode.MachineEncrypted);
        FileKeyring.ReadMode(_dir.Path).ShouldBe(SecretStorageMode.MachineEncrypted);
        File.Exists(_dir.File("secrets.json")).ShouldBeFalse();
        (await keyring.GetAsync("app.noto.server", "session")).ShouldBe("one");
        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBe("two");
    }

    [Fact]
    public async Task Switching_to_a_passphrase_rekeys_the_store()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);
        await keyring.SetAsync("app.noto.server", "session", "one");

        await keyring.SwitchModeAsync(SecretStorageMode.PassphraseEncrypted, "hunter2");
        File.Exists(_dir.File("secrets.enc")).ShouldBeFalse();
        File.Exists(_dir.File("secrets.vault")).ShouldBeTrue();
        (await keyring.GetAsync("app.noto.server", "session")).ShouldBe("one");

        var reopened = Open(SecretStorageMode.PassphraseEncrypted);
        await Should.ThrowAsync<SecretStorageException>(() => reopened.UnlockAsync("nope"));
        await reopened.UnlockAsync("hunter2");
        (await reopened.GetAsync("app.noto.server", "session")).ShouldBe("one");
    }

    [Fact]
    public async Task Switching_to_a_passphrase_needs_one_and_changes_nothing()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);
        await keyring.SetAsync("app.noto.server", "session", "one");

        await Should.ThrowAsync<SecretStorageException>(() =>
            keyring.SwitchModeAsync(SecretStorageMode.PassphraseEncrypted)
        );

        keyring.Mode.ShouldBe(SecretStorageMode.MachineEncrypted);
        FileKeyring.ReadMode(_dir.Path).ShouldBe(SecretStorageMode.MachineEncrypted);
        (await keyring.GetAsync("app.noto.server", "session")).ShouldBe("one");
    }

    [Fact]
    public async Task A_locked_vault_cannot_be_switched_away_from()
    {
        var keyring = Open(SecretStorageMode.PassphraseEncrypted);
        await keyring.UnlockAsync("right");
        await keyring.SetAsync("app.noto.server", "session", "one");

        var reopened = Open(SecretStorageMode.PassphraseEncrypted);
        var error = await Should.ThrowAsync<SecretStorageException>(() =>
            reopened.SwitchModeAsync(SecretStorageMode.PlainFile)
        );
        error.Message.ShouldContain("Unlock");

        reopened.Mode.ShouldBe(SecretStorageMode.PassphraseEncrypted);
        FileKeyring.ReadMode(_dir.Path).ShouldBe(SecretStorageMode.PassphraseEncrypted);
        File.Exists(_dir.File("secrets.vault")).ShouldBeTrue();
    }

    [Fact]
    public async Task Os_keyring_mode_delegates_and_can_be_left_without_losing_keys()
    {
        var os = new FakeOsKeyring();
        var keyring = Open(SecretStorageMode.OsKeyring, os);
        await keyring.SetAsync("app.noto.server", "session", "one");

        // The secret really is in the keychain, not in a file beside the database.
        (await os.GetAsync("app.noto.server", "session")).ShouldBe("one");
        File.Exists(_dir.File("secrets.enc")).ShouldBeFalse();
        File.Exists(_dir.File("secrets.json")).ShouldBeFalse();

        await keyring.SwitchModeAsync(SecretStorageMode.MachineEncrypted);

        (await os.GetAsync("app.noto.server", "session")).ShouldBeNull(); // the keychain copy is gone
        (await keyring.GetAsync("app.noto.server", "session")).ShouldBe("one"); // but the key came along
        File.Exists(_dir.File("secrets.index.json")).ShouldBeFalse();
    }

    [Fact]
    public void Os_keyring_mode_is_not_capable_when_the_platform_has_no_keychain()
    {
        var keyring = Open(SecretStorageMode.OsKeyring, new InMemoryKeyring());
        keyring.Capability.IsSupported.ShouldBeFalse();
        keyring.Capability.Reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Secrets_are_written_atomically_and_readable_only_by_their_owner()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);
        await keyring.SetAsync("app.noto.ai", "api-key", "token");

        Directory.GetFiles(_dir.Path, "*.tmp").ShouldBeEmpty();
        if (OperatingSystem.IsWindows())
            return;

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.GetUnixFileMode(_dir.File("secrets.enc")).ShouldBe(ownerOnly);
        File.GetUnixFileMode(_keyFile).ShouldBe(ownerOnly);
    }

    [Fact]
    public async Task A_store_that_was_never_written_is_simply_empty()
    {
        var keyring = Open(SecretStorageMode.MachineEncrypted);
        (await keyring.GetAsync("app.noto.ai", "api-key")).ShouldBeNull();
        await keyring.DeleteAsync("app.noto.ai", "api-key"); // deleting from nothing is a no-op
    }
}
