using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Noto.Platform.Linux;

[SupportedOSPlatform("linux")]
static class X11Native
{
    const string Lib = "libX11.so.6";

    public const int KeyPress = 2;
    public const int GrabModeAsync = 1;
    public const int XEventSize = 192; // sizeof(XEvent): a 24-long union on 64-bit

    public delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);

    public static bool IsAvailable() => NativeLibrary.TryLoad(Lib, out var h) && Free(h);

    static bool Free(IntPtr handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }

    [DllImport(Lib)]
    public static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(Lib)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(Lib)]
    public static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(Lib)]
    public static extern UIntPtr XStringToKeysym(string name);

    [DllImport(Lib)]
    public static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);

    [DllImport(Lib)]
    public static extern int XGrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        IntPtr grabWindow,
        bool ownerEvents,
        int pointerMode,
        int keyboardMode
    );

    [DllImport(Lib)]
    public static extern int XUngrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        IntPtr grabWindow
    );

    [DllImport(Lib)]
    public static extern int XPending(IntPtr display);

    [DllImport(Lib)]
    public static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport(Lib)]
    public static extern int XSync(IntPtr display, bool discard);

    [DllImport(Lib)]
    public static extern IntPtr XSetErrorHandler(ErrorHandler handler);

    // Puts back the handler pointer a previous XSetErrorHandler call returned.
    [DllImport(Lib, EntryPoint = "XSetErrorHandler")]
    public static extern IntPtr RestoreErrorHandler(IntPtr previous);
}
