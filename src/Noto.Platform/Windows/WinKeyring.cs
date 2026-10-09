using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Noto.Platform.Abstractions;

namespace Noto.Platform.Windows;

// Windows Credential Manager generic credentials, target "<service>/<account>". Persisted to this machine only
// (not roamed), and encrypted by DPAPI for the signed-in user.
[SupportedOSPlatform("windows")]
public sealed class WinKeyring : IKeyring
{
    const uint TypeGeneric = 1;
    const uint PersistLocalMachine = 2;
    const int MaxBlobBytes = 5 * 512; // CRED_MAX_CREDENTIAL_BLOB_SIZE

    public Capability Capability => Capability.Supported;

    public Task SetAsync(string service, string account, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length > MaxBlobBytes)
            throw new InvalidOperationException(
                $"Windows Credential Manager stores at most {MaxBlobBytes} bytes per secret."
            );

        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new WinNative.Credential
            {
                Type = TypeGeneric,
                TargetName = Target(service, account),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = account,
            };
            if (!WinNative.CredWriteW(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string service, string account)
    {
        if (!WinNative.CredReadW(Target(service, account), TypeGeneric, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            return error == WinNative.ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<WinNative.Credential>(ptr);
            var bytes = new byte[credential.CredentialBlobSize];
            if (bytes.Length > 0)
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            WinNative.CredFree(ptr);
        }
    }

    public Task DeleteAsync(string service, string account)
    {
        if (!WinNative.CredDeleteW(Target(service, account), TypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != WinNative.ErrorNotFound)
                throw new Win32Exception(error);
        }
        return Task.CompletedTask;
    }

    static string Target(string service, string account) => $"{service}/{account}";
}
