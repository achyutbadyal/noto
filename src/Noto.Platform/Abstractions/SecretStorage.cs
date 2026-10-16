namespace Noto.Platform.Abstractions;

// How provider credentials and the sync session are kept on this device. The mode is a device setting,
// not a synced one, and it is remembered in `storage-mode.json` (docs/05 › ADR-06).
//
// MachineEncrypted is the default because it needs no user action and, unlike the OS keyring, it survives
// a rebuild: an ad-hoc signed macOS app gets a new CDHash every build, which silently invalidates every
// keychain ACL and brings back the "Noto wants to use your confidential information" sheet on each update.
public enum SecretStorageMode
{
    // AES-GCM under a key derived from this machine's device id and a local keyfile.
    MachineEncrypted = 0,

    // AES-GCM under a key derived from a master passphrase the user types. Never written to disk.
    PassphraseEncrypted = 1,

    // Unencrypted JSON. Only sensible on a machine the user fully controls.
    PlainFile = 2,

    // macOS Keychain, Windows Credential Manager, or libsecret.
    OsKeyring = 3,
}

// A store the user has to fix — a wrong master passphrase, or a write attempted while locked — as opposed
// to one that simply has nothing in it. The settings pane catches this and asks again.
public sealed class SecretStorageException(string message) : Exception(message);
