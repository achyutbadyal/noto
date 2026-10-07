using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Noto.Platform.Abstractions;

namespace Noto.Platform.MacOS;

// Login keychain generic passwords: service "app.noto.connections", account = connection id (docs/04 §6).
[SupportedOSPlatform("macos")]
public sealed class MacKeyring : IKeyring
{
    public Capability Capability => Capability.Supported;

    public Task SetAsync(string service, string account, string secret)
    {
        var (svc, acc, pwd) = (Bytes(service), Bytes(account), Bytes(secret));
        var status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)svc.Length, svc, (uint)acc.Length, acc, out var len, out var data, out var item);

        if (status == 0)
        {
            Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
            status = Native.SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)pwd.Length, pwd);
            Native.CFRelease(item);
        }
        else if (status == Native.ErrSecItemNotFound)
        {
            status = Native.SecKeychainAddGenericPassword(IntPtr.Zero, (uint)svc.Length, svc, (uint)acc.Length, acc, (uint)pwd.Length, pwd, IntPtr.Zero);
        }

        Check(status);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string service, string account)
    {
        var (svc, acc) = (Bytes(service), Bytes(account));
        var status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)svc.Length, svc, (uint)acc.Length, acc, out var len, out var data, out var item);
        if (status == Native.ErrSecItemNotFound) return Task.FromResult<string?>(null);
        Check(status);

        var secret = new byte[len];
        Marshal.Copy(data, secret, 0, (int)len);
        Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
        Native.CFRelease(item);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(secret));
    }

    public Task DeleteAsync(string service, string account)
    {
        var (svc, acc) = (Bytes(service), Bytes(account));
        var status = Native.SecKeychainFindGenericPassword(IntPtr.Zero, (uint)svc.Length, svc, (uint)acc.Length, acc, out _, out var data, out var item);
        if (status == Native.ErrSecItemNotFound) return Task.CompletedTask;
        Check(status);

        Native.SecKeychainItemFreeContent(IntPtr.Zero, data);
        status = Native.SecKeychainItemDelete(item);
        Native.CFRelease(item);
        Check(status);
        return Task.CompletedTask;
    }

    static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"Keychain error {status}");
    }
}
