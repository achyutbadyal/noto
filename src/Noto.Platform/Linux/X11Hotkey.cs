using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.Linux;

// XGrabKey on the root window, on a thread with its own display connection. Only works on X11 (including
// XWayland-hosted sessions run as X11): a Wayland compositor does not let a client grab keys globally.
[SupportedOSPlatform("linux")]
public sealed class X11Hotkey : IGlobalHotkey
{
    const string WaylandReason =
        "Wayland doesn't allow global hotkeys. Bind a system shortcut to `noto --capture` instead.";
    const string NoX11Reason =
        "Global hotkeys need X11 (libX11 wasn't found). Bind a system shortcut to `noto --capture` instead.";

    static readonly X11Native.ErrorHandler GrabErrorHandler = (_, _) =>
    {
        Volatile.Write(ref _grabFailed, 1);
        return 0;
    };
    static int _grabFailed;

    Thread? _thread;
    CancellationTokenSource? _stop;

    public X11Hotkey()
    {
        Capability = Detect();
    }

    public Capability Capability { get; }

    static Capability Detect()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var wayland =
            string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (wayland)
            return Capability.Unsupported(WaylandReason);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return Capability.Unsupported("No display is available for a global hotkey.");
        return X11Native.IsAvailable() ? Capability.Supported : Capability.Unsupported(NoX11Reason);
    }

    public bool Register(HotkeyGesture gesture, Action onPressed)
    {
        Unregister();
        if (!Capability.IsSupported || X11Keys.KeysymName(gesture.Key) is not { } name)
            return false;

        var modifiers = X11Keys.Modifiers(gesture.Modifiers);
        var stop = new CancellationTokenSource();
        var ready = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var thread = new Thread(() => Pump(name, modifiers, onPressed, ready, stop.Token))
        {
            IsBackground = true,
            Name = "Noto global hotkey",
        };
        thread.Start();

        if (!ready.Task.Wait(TimeSpan.FromSeconds(3)) || !ready.Task.Result)
        {
            stop.Cancel();
            thread.Join(TimeSpan.FromSeconds(1));
            return false;
        }
        (_thread, _stop) = (thread, stop);
        return true;
    }

    public void Unregister()
    {
        _stop?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(1));
        (_thread, _stop) = (null, null);
    }

    public void Dispose() => Unregister();

    static void Pump(
        string keysym,
        uint modifiers,
        Action onPressed,
        TaskCompletionSource<bool> ready,
        CancellationToken stop
    )
    {
        var display = IntPtr.Zero;
        try
        {
            display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                ready.TrySetResult(false);
                return;
            }
            var root = X11Native.XDefaultRootWindow(display);
            var keycode = X11Native.XKeysymToKeycode(display, X11Native.XStringToKeysym(keysym));
            if (keycode == 0)
            {
                ready.TrySetResult(false);
                return;
            }

            var variants = X11Keys.WithLockVariants(modifiers);
            ready.TrySetResult(Grab(display, root, keycode, variants));
            if (!ready.Task.Result)
                return;

            var buffer = Marshal.AllocHGlobal(X11Native.XEventSize);
            try
            {
                // Poll rather than block in XNextEvent so Unregister can end the loop without a wake-up message.
                while (!stop.IsCancellationRequested)
                {
                    while (X11Native.XPending(display) > 0)
                    {
                        X11Native.XNextEvent(display, buffer);
                        if (Marshal.ReadInt32(buffer) == X11Native.KeyPress)
                            onPressed();
                    }
                    stop.WaitHandle.WaitOne(40);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                foreach (var m in variants)
                    X11Native.XUngrabKey(display, keycode, m, root);
                X11Native.XSync(display, false);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            ready.TrySetResult(false);
        }
        finally
        {
            if (display != IntPtr.Zero)
                X11Native.XCloseDisplay(display);
        }
    }

    // XGrabKey reports a taken chord (BadAccess) asynchronously, and Xlib's default handler exits the process.
    // The handler is swapped in only around the grab, and XSync flushes the error before it is restored.
    static bool Grab(IntPtr display, IntPtr root, byte keycode, IReadOnlyList<uint> variants)
    {
        Volatile.Write(ref _grabFailed, 0);
        var previous = X11Native.XSetErrorHandler(GrabErrorHandler);
        try
        {
            foreach (var m in variants)
                X11Native.XGrabKey(
                    display,
                    keycode,
                    m,
                    root,
                    false,
                    X11Native.GrabModeAsync,
                    X11Native.GrabModeAsync
                );
            X11Native.XSync(display, false);
        }
        finally
        {
            X11Native.RestoreErrorHandler(previous);
        }
        return Volatile.Read(ref _grabFailed) == 0;
    }
}
