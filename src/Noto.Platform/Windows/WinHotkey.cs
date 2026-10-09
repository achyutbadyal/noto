using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.Windows;

// RegisterHotKey with no window posts WM_HOTKEY to the registering thread's queue, so the hot key lives on its own
// thread with a message loop. The callback runs on that thread; callers marshal to the UI thread.
[SupportedOSPlatform("windows")]
public sealed class WinHotkey : IGlobalHotkey
{
    const int HotkeyId = 1;

    Thread? _thread;
    uint _threadId;

    public Capability Capability => Capability.Supported;

    public bool Register(HotkeyGesture gesture, Action onPressed)
    {
        Unregister();
        if (!WindowsKeys.TryGetVirtualKey(gesture.Key, out var vk))
            return false;

        var modifiers = WindowsKeys.Modifiers(gesture.Modifiers);
        var ready = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var thread = new Thread(() => Pump(modifiers, vk, onPressed, ready))
        {
            IsBackground = true,
            Name = "Noto global hotkey",
        };
        thread.Start();

        if (!ready.Task.Wait(TimeSpan.FromSeconds(2)) || !ready.Task.Result)
        {
            Stop(thread);
            return false;
        }
        _thread = thread;
        return true;
    }

    public void Unregister()
    {
        var thread = _thread;
        _thread = null;
        if (thread is not null)
            Stop(thread);
    }

    public void Dispose() => Unregister();

    void Pump(uint modifiers, uint vk, Action onPressed, TaskCompletionSource<bool> ready)
    {
        // Touching the queue creates it, so WM_QUIT posted by Unregister cannot be lost.
        WinNative.PeekMessageW(out _, IntPtr.Zero, 0, 0, WinNative.PmNoRemove);
        _threadId = WinNative.GetCurrentThreadId();

        // Fails if another app already owns the chord.
        var registered = WinNative.RegisterHotKey(IntPtr.Zero, HotkeyId, modifiers, vk);
        ready.TrySetResult(registered);
        if (!registered)
            return;

        try
        {
            while (WinNative.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.Message == WinNative.WmHotkey)
                    onPressed();
        }
        finally
        {
            WinNative.UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    void Stop(Thread thread)
    {
        if (_threadId != 0)
            WinNative.PostThreadMessageW(_threadId, WinNative.WmQuit, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(1));
        _threadId = 0;
    }
}
