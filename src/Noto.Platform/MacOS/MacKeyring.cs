using System.Runtime.Versioning;
using System.Text;
using Noto.Platform.Abstractions;

namespace Noto.Platform.MacOS;

// Login keychain generic passwords: service "app.noto.connections", account = connection id (docs/04 §6).
// Items are not synchronizable, so they never reach iCloud Keychain, and they are readable only after the
// first unlock on this device.
[SupportedOSPlatform("macos")]
public sealed class MacKeyring : IKeyring
{
    public Capability Capability => Capability.Supported;

    public Task SetAsync(string service, string account, string secret)
    {
        var data = Encoding.UTF8.GetBytes(secret);
        using var item = MacKeychain
            .Dict.Item(service, account)
            .SetData(MacKeychain.KSecValueData, data)
            .Set(MacKeychain.KSecAttrSynchronizable, MacKeychain.KCFBooleanFalse)
            .Set(
                MacKeychain.KSecAttrAccessible,
                MacKeychain.KSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            );

        var status = MacKeychain.Add(item.Handle);
        if (MacKeychain.IsDuplicate(status))
        {
            using var query = MacKeychain.Dict.Item(service, account);
            using var update = new MacKeychain.Dict().SetData(MacKeychain.KSecValueData, data);
            status = MacKeychain.Update(query.Handle, update.Handle);
        }
        MacKeychain.Check(status);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string service, string account)
    {
        using var query = MacKeychain
            .Dict.Item(service, account)
            .Set(MacKeychain.KSecReturnData, MacKeychain.KCFBooleanTrue)
            .Set(MacKeychain.KSecMatchLimit, MacKeychain.KSecMatchLimitOne);

        var bytes = MacKeychain.CopyData(query.Handle);
        return Task.FromResult(bytes is null ? null : Encoding.UTF8.GetString(bytes));
    }

    public Task DeleteAsync(string service, string account)
    {
        using var query = MacKeychain.Dict.Item(service, account);
        var status = MacKeychain.Delete(query.Handle);
        if (!MacKeychain.IsNotFound(status))
            MacKeychain.Check(status);
        return Task.CompletedTask;
    }
}
