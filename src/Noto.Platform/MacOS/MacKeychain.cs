using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Noto.Platform.MacOS;

// SecItem* interop for generic passwords. The legacy SecKeychain* API is deprecated, so this uses the
// CFDictionary-based calls. Items stay in the login keychain (no data-protection keychain, which needs
// a signed app entitlement) and are never marked synchronizable, so iCloud Keychain does not copy them.
[SupportedOSPlatform("macos")]
static class MacKeychain
{
    const string SecurityPath = "/System/Library/Frameworks/Security.framework/Security";
    const string CoreFoundationPath =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    const int ErrSecItemNotFound = -25300;
    const int ErrSecDuplicateItem = -25299;
    const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    static readonly IntPtr SecurityLib = NativeLibrary.Load(SecurityPath);
    static readonly IntPtr CoreFoundationLib = NativeLibrary.Load(CoreFoundationPath);

    // Dictionary callbacks are structs exported by address; the rest are CF object globals, read by value.
    static readonly IntPtr KeyCallbacks = NativeLibrary.GetExport(
        CoreFoundationLib,
        "kCFTypeDictionaryKeyCallBacks"
    );
    static readonly IntPtr ValueCallbacks = NativeLibrary.GetExport(
        CoreFoundationLib,
        "kCFTypeDictionaryValueCallBacks"
    );

    public static IntPtr KSecClass => Global(SecurityLib, "kSecClass");
    public static IntPtr KSecClassGenericPassword =>
        Global(SecurityLib, "kSecClassGenericPassword");
    public static IntPtr KSecAttrService => Global(SecurityLib, "kSecAttrService");
    public static IntPtr KSecAttrAccount => Global(SecurityLib, "kSecAttrAccount");
    public static IntPtr KSecValueData => Global(SecurityLib, "kSecValueData");
    public static IntPtr KSecReturnData => Global(SecurityLib, "kSecReturnData");
    public static IntPtr KSecMatchLimit => Global(SecurityLib, "kSecMatchLimit");
    public static IntPtr KSecMatchLimitOne => Global(SecurityLib, "kSecMatchLimitOne");
    public static IntPtr KSecAttrSynchronizable => Global(SecurityLib, "kSecAttrSynchronizable");
    public static IntPtr KSecAttrAccessible => Global(SecurityLib, "kSecAttrAccessible");
    public static IntPtr KSecAttrAccessibleAfterFirstUnlockThisDeviceOnly =>
        Global(SecurityLib, "kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly");
    public static IntPtr KCFBooleanTrue => Global(CoreFoundationLib, "kCFBooleanTrue");
    public static IntPtr KCFBooleanFalse => Global(CoreFoundationLib, "kCFBooleanFalse");

    // A query or attribute dictionary, plus every CF object it created, released together.
    public sealed class Dict : IDisposable
    {
        readonly List<IntPtr> _owned = [];
        public IntPtr Handle { get; } =
            CFDictionaryCreateMutable(IntPtr.Zero, 0, KeyCallbacks, ValueCallbacks);

        public Dict Set(IntPtr key, IntPtr value)
        {
            CFDictionarySetValue(Handle, key, value);
            return this;
        }

        public Dict SetOwned(IntPtr key, IntPtr ownedValue)
        {
            _owned.Add(ownedValue);
            return Set(key, ownedValue);
        }

        public Dict SetString(IntPtr key, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return SetOwned(
                key,
                CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, Utf8, 0)
            );
        }

        public Dict SetData(IntPtr key, byte[] value) =>
            SetOwned(key, CFDataCreate(IntPtr.Zero, value, value.Length));

        // Base query: one generic password, identified by service and account.
        public static Dict Item(string service, string account) =>
            new Dict()
                .Set(KSecClass, KSecClassGenericPassword)
                .SetString(KSecAttrService, service)
                .SetString(KSecAttrAccount, account);

        public void Dispose()
        {
            foreach (var r in _owned)
                CFRelease(r);
            CFRelease(Handle);
        }
    }

    public static int Add(IntPtr attributes) => SecItemAdd(attributes, IntPtr.Zero);

    public static int Update(IntPtr query, IntPtr attributes) => SecItemUpdate(query, attributes);

    public static int Delete(IntPtr query) => SecItemDelete(query);

    // Returns the stored bytes, or null if no item matches.
    public static byte[]? CopyData(IntPtr query)
    {
        var status = SecItemCopyMatching(query, out var result);
        if (status == ErrSecItemNotFound)
            return null;
        Check(status);

        try
        {
            var length = CFDataGetLength(result);
            var bytes = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, (int)length);
            return bytes;
        }
        finally
        {
            CFRelease(result);
        }
    }

    public static bool IsDuplicate(int status) => status == ErrSecDuplicateItem;

    public static bool IsNotFound(int status) => status == ErrSecItemNotFound;

    public static void Check(int status)
    {
        if (status != 0)
            throw new InvalidOperationException($"Keychain error {status}");
    }

    static IntPtr Global(IntPtr lib, string name) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(lib, name));

    [DllImport(SecurityPath)]
    static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityPath)]
    static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityPath)]
    static extern int SecItemDelete(IntPtr query);

    [DllImport(SecurityPath)]
    static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(CoreFoundationPath)]
    static extern IntPtr CFDictionaryCreateMutable(
        IntPtr allocator,
        nint capacity,
        IntPtr keyCallBacks,
        IntPtr valueCallBacks
    );

    [DllImport(CoreFoundationPath)]
    static extern void CFDictionarySetValue(IntPtr dict, IntPtr key, IntPtr value);

    [DllImport(CoreFoundationPath)]
    static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationPath)]
    static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationPath)]
    static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationPath)]
    static extern IntPtr CFStringCreateWithBytes(
        IntPtr allocator,
        byte[] bytes,
        nint numBytes,
        uint encoding,
        byte isExternalRepresentation
    );

    [DllImport(CoreFoundationPath)]
    static extern void CFRelease(IntPtr cf);
}
