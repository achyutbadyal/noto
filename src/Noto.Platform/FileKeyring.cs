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
// a keyfile, so it needs no user action and never touches the OS keychain. That matters because an ad-hoc
// signed macOS build gets a fresh CDHash every time, which invalidates every keychain ACL and makes the
// system ask for permission again after each update (docs/05 › ADR-06).
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
    public static SecretStorageMode ReadMode(string dataDirectory)
    {
        try
        {
            var path = ConfigPath(dataDirectory);
            if (!File.Exists(path))
                return SecretStorageMode.MachineEncrypted;
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                KeyringJson.Default.StorageModeFile
            );
            return Enum.TryParse<SecretStorageMode>(file?.Mode, out var mode)
                ? mode
                : SecretStorageMode.MachineEncrypted;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return SecretStorageMode.MachineEncrypted;
        }
    }

    public static void WriteMode(string dataDirectory, SecretStorageMode mode)
    {
        Directory.CreateDirectory(dataDirectory);
        WriteFile(
            ConfigPath(dataDirectory),
            JsonSerializer.SerializeToUtf8Bytes(
                new StorageModeFile { Mode = mode.ToString() },
                KeyringJson.Default.StorageModeFile
            )
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

            var key = Key(service, account);
            var entries = await LoadAllAsync(_mode, _passphrase);
            return entries.TryGetValue(key, out var found)
                ? found
                : await MigrateAsync(service, account, key, entries);
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
        }
        finally
        {
            _gate.Release();
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
    // mode the user just left. Nothing is committed until the new store has been written.
    public async Task SwitchModeAsync(SecretStorageMode mode, string? passphrase = null)
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
                return;
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

    // ---- Lazy migration out of the OS keychain -------------------------------

    // The first machine-encrypted read of a key that only the OS keychain has. One lookup per key, and the
    // copy is removed from the keychain afterwards, so the consent sheet stops coming back (docs/05 ›
    // ADR-06). Fail-open: an unavailable keyring simply means the key is not there.
    async Task<string?> MigrateAsync(
        string service,
        string account,
        string key,
        Dictionary<string, string> entries
    )
    {
        if (_mode != SecretStorageMode.MachineEncrypted || !_os.Capability.IsSupported)
            return null;
        if (await _os.GetAsync(service, account) is not { } secret)
            return null;

        entries[key] = secret;
        await SaveAllAsync(entries, _mode, _passphrase);
        await _os.DeleteAsync(service, account);
        return secret;
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

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(StorageModeFile))]
sealed partial class KeyringJson : JsonSerializerContext;

// `storage-mode.json` — which store this device uses. Not a secret.
sealed class StorageModeFile
{
    public string? Mode { get; set; }
}
