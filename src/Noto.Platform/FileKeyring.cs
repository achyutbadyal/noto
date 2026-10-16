using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noto.Platform.Abstractions;

namespace Noto.Platform;

// The device's secret store, in whichever of the four `SecretStorageMode`s the user picked. One class,
// switched on the mode — the file formats differ but the shape of every operation is the same, so a
// subclass per mode would only spread the locking and the atomic write over four files.
//
// MachineEncrypted is the default: the JSON is sealed with a key derived from this machine's device id and
// a keyfile, so it needs no user action and no OS keychain. That matters because an ad-hoc signed macOS
// build gets a fresh CDHash every time, which invalidates every keychain ACL and makes the system ask for
// permission again after each update (docs/05 › ADR-06).
//
// The OS keyring is therefore never on a read path in the three file modes. It is consulted only by
// `SweepAsync` — once per install, to move what the builds that wrote there directly left behind into the
// file and delete it — and best-effort by `DeleteAsync`, so a token cannot outlive its connection.
public sealed class FileKeyring : IKeyring
{
    const int NonceBytes = 12;
    const int TagBytes = 16;
    const int SaltBytes = 16;
    const int KeyBytes = 32;
    const int KeyfileBytes = 32;

    // 210k iterations of SHA-256: slow enough that guessing a passphrase costs real time.
    const int PassphraseIterations = 210_000;
    const string KeyInfo = "noto-secrets-machine-v1";

    // Service and account joined for the on-disk map. A control character cannot appear in either.
    const char Separator = '\u0001';

    readonly string _directory;
    readonly string _deviceId;
    readonly string _keyFilePath;

    // OsKeyring mode delegates here; the other three never touch it except for lazy migration.
    readonly IKeyring _os;

    // Every read and write goes through this: the store is a whole file, so a concurrent set would
    // otherwise read-modify-write over its neighbour.
    readonly SemaphoreSlim _gate = new(1, 1);

    SecretStorageMode _mode;
    string? _passphrase;

    public FileKeyring(
        string dataDirectory,
        string deviceId,
        IKeyring? osKeyring = null,
        string? keyFilePath = null
    )
    {
        _directory = dataDirectory;
        _deviceId = deviceId;
        _os = osKeyring ?? new InMemoryKeyring();
        _keyFilePath = keyFilePath ?? DefaultKeyFilePath();
        _mode = ReadMode(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
    }

    public SecretStorageMode Mode => _mode;

    // False only while a passphrase-encrypted store waits for the user to type one.
    public bool IsUnlocked =>
        _mode != SecretStorageMode.PassphraseEncrypted || _passphrase is not null;

    public Capability Capability =>
        _mode == SecretStorageMode.OsKeyring ? _os.Capability : Capability.Supported;

    // ---- Where things live ----------------------------------------------------

    static string StorePath(string directory, SecretStorageMode mode) =>
        Path.Combine(
            directory,
            mode switch
            {
                SecretStorageMode.PlainFile => "secrets.json",
                SecretStorageMode.PassphraseEncrypted => "secrets.vault",
                _ => "secrets.enc",
            }
        );

    // The names of the keys, never their values. Only the OS keyring needs it: none of the three platform
    // keyrings can list what they hold, and a live mode switch has to know what to copy.
    string IndexPath => Path.Combine(_directory, "secrets.index.json");

    static string ConfigPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "storage-mode.json");

    // Deliberately outside the data directory: backing up or syncing the app folder still doesn't carry
    // the key that opens the secrets.
    public static string DefaultKeyFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "noto",
            ".keyring.key"
        );

    // ---- The mode is a device setting -----------------------------------------

    // A missing or unreadable file means the default, so a half-written config never locks anyone out.
    public static SecretStorageMode ReadMode(string dataDirectory) =>
        ReadModeFile(dataDirectory) is { Mode: { } text }
        && Enum.TryParse<SecretStorageMode>(text, out var mode)
            ? mode
            : SecretStorageMode.MachineEncrypted;

    // True until this install has looked through the OS keyring once, which is what moves the items the
    // older builds wrote there into the file and stops the consent sheets (docs/05 › ADR-06).
    public static bool NeedsKeyringSweep(string dataDirectory) =>
        ReadModeFile(dataDirectory) is not { KeyringSwept: true };

    public static void MarkKeyringSwept(string dataDirectory)
    {
        var file = ReadModeFile(dataDirectory) ?? new StorageModeFile();
        // Never lose the mode: a config that could not be read reads as the default, and that is what gets
        // written back rather than an empty one.
        file.Mode ??= ReadMode(dataDirectory).ToString();
        file.KeyringSwept = true;
        WriteModeFile(dataDirectory, file);
    }

    // Writing the mode re-arms the sweep: a mode change is exactly when items can be left behind again.
    public static void WriteMode(string dataDirectory, SecretStorageMode mode) =>
        WriteModeFile(dataDirectory, new StorageModeFile { Mode = mode.ToString() });

    static StorageModeFile? ReadModeFile(string dataDirectory)
    {
        try
        {
            var path = ConfigPath(dataDirectory);
            return File.Exists(path)
                ? JsonSerializer.Deserialize(
                    File.ReadAllText(path),
                    KeyringJson.Default.StorageModeFile
                )
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }

    static void WriteModeFile(string dataDirectory, StorageModeFile file)
    {
        Directory.CreateDirectory(dataDirectory);
        WriteFile(
            ConfigPath(dataDirectory),
            JsonSerializer.SerializeToUtf8Bytes(file, KeyringJson.Default.StorageModeFile)
        );
    }

    // ---- IKeyring -------------------------------------------------------------

    public async Task SetAsync(string service, string account, string secret)
    {
        await _gate.WaitAsync();
        try
        {
            var key = Key(service, account);
            if (_mode == SecretStorageMode.OsKeyring)
            {
                await _os.SetAsync(service, account, secret);
                await AddToIndexAsync(key);
                return;
            }

            RequireUnlocked();
            var entries = await LoadAllAsync(_mode, _passphrase);
            entries[key] = secret;
            await SaveAllAsync(entries, _mode, _passphrase);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetAsync(string service, string account)
    {
        await _gate.WaitAsync();
        try
        {
            if (_mode == SecretStorageMode.OsKeyring)
                return await _os.GetAsync(service, account);

            // A locked store has nothing readable, and the session is restored through here at startup:
            // returning null keeps a passphrase the user hasn't typed yet from breaking the app.
            if (!IsUnlocked)
                return null;

            // The file, and only the file. The OS keyring is swept once (SweepAsync) and never consulted
            // on a read, so no read can raise a consent sheet at a moment the user didn't choose
            // (docs/05 › ADR-06).
            var entries = await LoadAllAsync(_mode, _passphrase);
            return entries.TryGetValue(Key(service, account), out var found) ? found : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string service, string account)
    {
        await _gate.WaitAsync();
        try
        {
            var key = Key(service, account);
            if (_mode == SecretStorageMode.OsKeyring)
            {
                await _os.DeleteAsync(service, account);
                await RemoveFromIndexAsync(key);
                return;
            }

            if (!IsUnlocked)
                return; // locked: nothing to remove, and nothing we could rewrite

            var entries = await LoadAllAsync(_mode, _passphrase);
            if (entries.Remove(key))
                await SaveAllAsync(entries, _mode, _passphrase);

            // A copy in the OS keyring would outlive the connection it belonged to, so the delete goes
            // there too. Best effort: a keyring that refuses must not make disconnecting fail.
            await TryDeleteFromOsAsync(service, account);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task TryDeleteFromOsAsync(string service, string account)
    {
        if (!_os.Capability.IsSupported)
            return;
        try
        {
            await _os.DeleteAsync(service, account);
        }
        catch (Exception)
        {
            // Denied or unavailable: `SweepAsync` is the retry path, not a failure the caller can act on.
        }
    }

    // ---- Passphrase and mode switching ---------------------------------------

    // Checks the passphrase by opening the store once, so a wrong one leaves it locked and the settings
    // pane can ask again instead of half-opening it.
    public async Task UnlockAsync(string passphrase)
    {
        if (_mode != SecretStorageMode.PassphraseEncrypted)
            return;

        await _gate.WaitAsync();
        try
        {
            await LoadAllAsync(_mode, passphrase);
            _passphrase = passphrase;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Copies every known key into the new store and wipes the old one, so nothing is left behind in the
    // mode the user just left. Nothing is committed until the new store has been written. `knownKeys` is
    // what the caller knows by name, for the sweep that follows (see SweepAsync).
    //
    // Returns what the sweep did, or null when there was nothing to sweep — the mode didn't change, or the
    // new mode is the keyring itself.
    public async Task<KeyringSweepReport?> SwitchModeAsync(
        SecretStorageMode mode,
        string? passphrase = null,
        IEnumerable<(string Service, string Account)>? knownKeys = null
    )
    {
        if (mode == SecretStorageMode.PassphraseEncrypted && string.IsNullOrEmpty(passphrase))
            throw new SecretStorageException("Choose a master passphrase.");

        await _gate.WaitAsync();
        try
        {
            if (mode == _mode)
            {
                // Re-applying the current mode is how a locked store gets unlocked.
                if (mode == SecretStorageMode.PassphraseEncrypted)
                {
                    await LoadAllAsync(mode, passphrase);
                    _passphrase = passphrase;
                }
                return null;
            }

            if (_mode == SecretStorageMode.PassphraseEncrypted && !IsUnlocked)
                throw new SecretStorageException(
                    "Unlock the vault before switching it to another mode."
                );

            var entries = await LoadAllAsync(_mode, _passphrase);
            await SaveAllAsync(entries, mode, passphrase);
            await WipeAsync(_mode);

            _mode = mode;
            _passphrase = mode == SecretStorageMode.PassphraseEncrypted ? passphrase : null;
            WriteMode(_directory, mode);

            // Leaving the keyring mode is not enough on its own: `WipeAsync` can only remove what the index
            // lists, and an install that upgraded from a build predating the index has items it never saw.
            // So the keys the caller knows about are swept here too. Never throws.
            return mode == SecretStorageMode.OsKeyring
                ? null
                : await SweepLockedAsync(knownKeys, once: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---- The store, as a whole ------------------------------------------------

    async Task<Dictionary<string, string>> LoadAllAsync(SecretStorageMode mode, string? passphrase)
    {
        if (mode == SecretStorageMode.OsKeyring)
        {
            var held = new Dictionary<string, string>();
            foreach (var key in await ReadIndexAsync())
            {
                var (service, account) = Split(key);
                if (await _os.GetAsync(service, account) is { } secret)
                    held[key] = secret;
            }
            return held;
        }

        var path = StorePath(_directory, mode);
        try
        {
            if (!File.Exists(path))
                return [];

            var bytes = await File.ReadAllBytesAsync(path);
            var json = mode switch
            {
                SecretStorageMode.PlainFile => bytes,
                SecretStorageMode.MachineEncrypted => DecryptMachine(bytes),
                _ => DecryptPassphrase(bytes, passphrase),
            };
            return JsonSerializer.Deserialize(json, KeyringJson.Default.DictionaryStringString)
                ?? [];
        }
        // A file we cannot open is an empty store — never a crash. The one case the user has to act on is
        // a wrong master passphrase, which is rethrown so the pane can say so instead of looking empty.
        catch (Exception e) when (e is IOException or JsonException or CryptographicException)
        {
            if (mode == SecretStorageMode.PassphraseEncrypted)
                throw new SecretStorageException(
                    "That master passphrase doesn't open the vault. Try again."
                );
            return [];
        }
    }

    async Task SaveAllAsync(
        Dictionary<string, string> entries,
        SecretStorageMode mode,
        string? passphrase
    )
    {
        if (mode == SecretStorageMode.OsKeyring)
        {
            foreach (var (key, secret) in entries)
            {
                var (service, account) = Split(key);
                await _os.SetAsync(service, account, secret);
            }
            await WriteIndexAsync([.. entries.Keys]);
            return;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(
            entries,
            KeyringJson.Default.DictionaryStringString
        );
        var bytes = mode switch
        {
            SecretStorageMode.PlainFile => json,
            SecretStorageMode.MachineEncrypted => EncryptMachine(json),
            _ => EncryptPassphrase(json, passphrase),
        };
        await WriteFileAsync(StorePath(_directory, mode), bytes);
    }

    async Task WipeAsync(SecretStorageMode mode)
    {
        if (mode == SecretStorageMode.OsKeyring)
        {
            foreach (var key in await ReadIndexAsync())
            {
                var (service, account) = Split(key);
                await _os.DeleteAsync(service, account);
            }
            await DeleteIndexAsync();
            return;
        }

        try
        {
            File.Delete(StorePath(_directory, mode));
        }
        catch (IOException) { }
    }

    // ---- The one-time sweep out of the OS keychain ----------------------------

    // Moves whatever the OS keyring still holds into the file store, then deletes it there, so an install
    // that once wrote to the keychain stops seeing consent sheets (docs/05 › ADR-06).
    //
    // This is deliberately NOT a fallback on a read. A lookup for a key we already hold raises the sheet
    // for an item we no longer need — the very thing MachineEncrypted exists to avoid — and one the user
    // cancels comes back on every launch, because the copy is only deleted after a read that succeeded.
    // Running once, at a moment the app chooses, makes the cost at most one sheet per key, once.
    //
    // `knownKeys` is what the caller knows by name: the sync session, the AI key, one entry per provider
    // connection. Every key already in the file is added, because a stale duplicate of a key we hold would
    // otherwise never be looked up and so never removed. Never throws — a keyring that refuses (a denied
    // sheet, a locked login keychain) is counted, not raised.
    //
    // Returns null when it could not look at all, which is also when the flag stays armed for a later
    // launch: a vault still waiting for its passphrase, `OsKeyring` mode, or a platform with no keyring.
    public async Task<KeyringSweepReport?> SweepOnceAsync(
        IEnumerable<(string Service, string Account)>? knownKeys = null
    )
    {
        await _gate.WaitAsync();
        try
        {
            return await SweepLockedAsync(knownKeys, once: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    // On demand, from Settings. Ignores the flag, because an install whose consent sheet was denied needs
    // a way back that doesn't depend on changing the mode.
    public async Task<KeyringSweepReport?> SweepAsync(
        IEnumerable<(string Service, string Account)>? knownKeys = null
    )
    {
        await _gate.WaitAsync();
        try
        {
            return await SweepLockedAsync(knownKeys, once: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // True when a sweep has something to compare: a file store that can be opened, and a keyring to look in.
    // Settings uses it to decide whether to offer the clean-up at all.
    public bool CanSweep =>
        _mode != SecretStorageMode.OsKeyring && IsUnlocked && _os.Capability.IsSupported;

    async Task<KeyringSweepReport?> SweepLockedAsync(
        IEnumerable<(string Service, string Account)>? knownKeys,
        bool once
    )
    {
        if (!CanSweep)
            return null;
        if (once && !NeedsKeyringSweep(_directory))
            return null;

        var entries = await LoadAllAsync(_mode, _passphrase);
        var candidates = new List<(string Service, string Account)>(knownKeys ?? []);
        candidates.AddRange(entries.Keys.Select(k => Split(k)));

        var moved = 0;
        var removed = 0;
        var failed = 0;
        foreach (var (service, account) in candidates.Distinct())
        {
            var key = Key(service, account);
            string? secret;
            try
            {
                secret = await _os.GetAsync(service, account);
            }
            catch (Exception)
            {
                failed++;
                continue;
            }

            if (secret is null)
                continue; // nothing of ours under that name

            if (!entries.ContainsKey(key))
            {
                entries[key] = secret;
                moved++;
            }

            try
            {
                await _os.DeleteAsync(service, account);
                removed++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        if (moved > 0)
            await SaveAllAsync(entries, _mode, _passphrase);

        // Recorded even when a key was refused: the promise is one sheet per key, once, and an automatic
        // retry is what made the sheets come back forever. Settings is the retry path.
        MarkKeyringSwept(_directory);
        return new KeyringSweepReport(moved, removed, failed);
    }

    // ---- Key index (OS keyring mode only) ------------------------------------

    async Task<List<string>> ReadIndexAsync()
    {
        try
        {
            return File.Exists(IndexPath)
                ? JsonSerializer.Deserialize(
                    await File.ReadAllTextAsync(IndexPath),
                    KeyringJson.Default.ListString
                ) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return [];
        }
    }

    Task WriteIndexAsync(List<string> keys) =>
        keys.Count == 0
            ? DeleteIndexAsync()
            : WriteFileAsync(
                IndexPath,
                JsonSerializer.SerializeToUtf8Bytes(keys, KeyringJson.Default.ListString)
            );

    Task DeleteIndexAsync()
    {
        try
        {
            File.Delete(IndexPath);
        }
        catch (IOException) { }
        return Task.CompletedTask;
    }

    async Task AddToIndexAsync(string key)
    {
        var keys = await ReadIndexAsync();
        if (keys.Contains(key))
            return;
        keys.Add(key);
        await WriteIndexAsync(keys);
    }

    async Task RemoveFromIndexAsync(string key)
    {
        var keys = await ReadIndexAsync();
        if (keys.Remove(key))
            await WriteIndexAsync(keys);
    }

    // ---- Crypto ---------------------------------------------------------------

    // [12B nonce][16B tag][ciphertext], sealed under a key that only this machine can derive.
    byte[] EncryptMachine(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(MachineKey(), TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    byte[] DecryptMachine(byte[] blob)
    {
        RequireLength(blob, NonceBytes + TagBytes, "Secret file");
        var plaintext = new byte[blob.Length - NonceBytes - TagBytes];
        using var aes = new AesGcm(MachineKey(), TagBytes);
        aes.Decrypt(
            blob.AsSpan(0, NonceBytes),
            blob.AsSpan(NonceBytes + TagBytes),
            blob.AsSpan(NonceBytes, TagBytes),
            plaintext
        );
        return plaintext;
    }

    // [16B salt][12B nonce][16B tag][ciphertext]. The salt is per write, so the derived key is too.
    byte[] EncryptPassphrase(byte[] plaintext, string? passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(PassphraseKey(passphrase, salt), TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. salt, .. nonce, .. tag, .. ciphertext];
    }

    byte[] DecryptPassphrase(byte[] blob, string? passphrase)
    {
        RequireLength(blob, SaltBytes + NonceBytes + TagBytes, "Secret vault");
        var plaintext = new byte[blob.Length - SaltBytes - NonceBytes - TagBytes];
        using var aes = new AesGcm(PassphraseKey(passphrase, blob[..SaltBytes]), TagBytes);
        aes.Decrypt(
            blob.AsSpan(SaltBytes, NonceBytes),
            blob.AsSpan(SaltBytes + NonceBytes + TagBytes),
            blob.AsSpan(SaltBytes + NonceBytes, TagBytes),
            plaintext
        );
        return plaintext;
    }

    // device.id + the keyfile. Neither the app data alone nor the keyfile alone opens the store, and no
    // salt is baked into the binary, so two installs never share a key.
    byte[] MachineKey()
    {
        var device = Encoding.UTF8.GetBytes(_deviceId);
        var keyfile = LoadOrCreateKeyFile();
        var ikm = new byte[device.Length + keyfile.Length];
        device.CopyTo(ikm, 0);
        keyfile.CopyTo(ikm, device.Length);
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm,
            KeyBytes,
            salt: null,
            info: Encoding.UTF8.GetBytes(KeyInfo)
        );
    }

    byte[] LoadOrCreateKeyFile()
    {
        if (File.Exists(_keyFilePath))
        {
            var existing = File.ReadAllBytes(_keyFilePath);
            // A truncated keyfile (an interrupted write) cannot open the old file, so a fresh one is
            // written and the unreadable store reads as empty.
            if (existing.Length == KeyfileBytes)
                return existing;
        }
        var created = RandomNumberGenerator.GetBytes(KeyfileBytes);
        WriteFile(_keyFilePath, created);
        return created;
    }

    static byte[] PassphraseKey(string? passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            passphrase ?? throw new SecretStorageException("Enter your master passphrase first."),
            salt,
            PassphraseIterations,
            HashAlgorithmName.SHA256,
            KeyBytes
        );

    static void RequireLength(byte[] blob, int minimum, string what)
    {
        if (blob.Length < minimum)
            throw new CryptographicException($"{what} is truncated.");
    }

    // ---- Files ---------------------------------------------------------------

    // Write beside the target, then rename over it: a reader never sees a half-written store.
    static async Task WriteFileAsync(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        Protect(temp);
        File.Move(temp, path, overwrite: true);
    }

    static void WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        Protect(temp);
        File.Move(temp, path, overwrite: true);
    }

    // 0600 on Unix: only the owner can read the file back.
    static void Protect(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // ---- Keys ----------------------------------------------------------------

    static string Key(string service, string account) => service + Separator + account;

    static (string Service, string Account) Split(string key)
    {
        var at = key.IndexOf(Separator);
        return at < 0 ? (key, "") : (key[..at], key[(at + 1)..]);
    }

    void RequireUnlocked()
    {
        if (!IsUnlocked)
            throw new SecretStorageException("Enter your master passphrase in Settings first.");
    }
}

// What one pass over the OS keyring did, so the settings pane can say something true about it rather than
// that it "succeeded". `Failed` counts keys the keyring would not hand over — a denied consent sheet, a
// locked login keychain — which are still in the keychain afterwards.
public sealed record KeyringSweepReport(int Moved, int Removed, int Failed);

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(StorageModeFile))]
sealed partial class KeyringJson : JsonSerializerContext;

// `storage-mode.json` — which store this device uses, and whether the one-time keyring sweep has run. Not
// a secret.
sealed class StorageModeFile
{
    public string? Mode { get; set; }

    // False until this install has looked through the OS keyring once. `WriteMode` clears it, so changing
    // the mode re-arms the sweep — that is when items can be left behind again.
    public bool KeyringSwept { get; set; }
}
