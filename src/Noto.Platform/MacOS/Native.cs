using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Noto.Platform.MacOS;

[SupportedOSPlatform("macos")]
static class Native
{
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string ObjC = "/usr/lib/libobjc.dylib";

    // Carbon hot keys
    [StructLayout(LayoutKind.Sequential)]
    public struct EventHotKeyID
    {
        public uint Signature;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    public delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(Carbon)]
    public static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    public static extern int InstallEventHandler(
        IntPtr target,
        EventHandlerProc handler,
        uint numTypes,
        EventTypeSpec[] list,
        IntPtr userData,
        out IntPtr handlerRef
    );

    [DllImport(Carbon)]
    public static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    public static extern int RegisterEventHotKey(
        uint keyCode,
        uint modifiers,
        EventHotKeyID id,
        IntPtr target,
        uint options,
        out IntPtr hotKeyRef
    );

    [DllImport(Carbon)]
    public static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    // Objective-C runtime (reduce-motion lookup)
    [DllImport(ObjC)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern byte MsgSendBool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libSystem.dylib")]
    public static extern IntPtr dlopen(string path, int mode);
}
