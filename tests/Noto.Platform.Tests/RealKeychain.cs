using Noto.Platform.Abstractions;
using Noto.Platform.Linux;
using Noto.Platform.MacOS;
using Noto.Platform.Windows;

namespace Noto.Platform.Tests;

// The OS keyrings need a real, unlocked desktop session. A CI runner has none and a sandbox has none
// either — macOS answers "Keychain error 100001" — so these tests probe once and skip where the keyring
// isn't usable, instead of failing on a machine that simply has no keychain (docs/05 › ADR-06).
//
// The app itself no longer depends on any of this: FileKeyring's default mode is machine-encrypted, and
// the OS keyring is only the one-time source for lazy migration.
internal static class RealKeychain
{
    static readonly Lazy<bool> Probe = new(UsableHere);

    public static bool Available => Environment.GetEnvironmentVariable("CI") is null && Probe.Value;

    static bool UsableHere()
    {
        var keyring = Current();
        if (keyring is null)
            return false;

        // A name nothing else uses, so a probe that can't clean up leaves no trace of the app.
        var service = $"app.noto.probe.{Guid.NewGuid():N}";
        try
        {
            keyring.SetAsync(service, "probe", "1").GetAwaiter().GetResult();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try
            {
                keyring.DeleteAsync(service, "probe").GetAwaiter().GetResult();
            }
            catch (Exception) { }
        }
    }

    static IKeyring? Current()
    {
        if (OperatingSystem.IsMacOS())
            return new MacKeyring();
        if (OperatingSystem.IsWindows())
            return new WinKeyring();
        if (OperatingSystem.IsLinux())
            return SecretToolKeyring.Create();
        return null;
    }
}
