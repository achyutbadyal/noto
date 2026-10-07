using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.MacOS;

// Carbon RegisterEventHotKey: works without Accessibility permission, but needs the app's event loop running.
[SupportedOSPlatform("macos")]
public sealed class MacHotkey : IGlobalHotkey
{
    const uint KeyboardClass = 0x6B657962; // 'keyb'
    const uint HotKeyPressed = 5;
    const uint Signature = 0x4E4F544F; // 'NOTO'

    // ANSI virtual key codes.
    static readonly Dictionary<string, uint> KeyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = 0,
        ["S"] = 1,
        ["D"] = 2,
        ["F"] = 3,
        ["H"] = 4,
        ["G"] = 5,
        ["Z"] = 6,
        ["X"] = 7,
        ["C"] = 8,
        ["V"] = 9,
        ["B"] = 11,
        ["Q"] = 12,
        ["W"] = 13,
        ["E"] = 14,
        ["R"] = 15,
        ["Y"] = 16,
        ["T"] = 17,
        ["1"] = 18,
        ["2"] = 19,
        ["3"] = 20,
        ["4"] = 21,
        ["6"] = 22,
        ["5"] = 23,
        ["9"] = 25,
        ["7"] = 26,
        ["8"] = 28,
        ["0"] = 29,
        ["O"] = 31,
        ["U"] = 32,
        ["I"] = 34,
        ["P"] = 35,
        ["L"] = 37,
        ["J"] = 38,
        ["K"] = 40,
        ["N"] = 45,
        ["M"] = 46,
        ["Space"] = 49,
    };

    Native.EventHandlerProc? _handler; // kept alive for the native callback
    IntPtr _handlerRef;
    IntPtr _hotKeyRef;
    Action? _onPressed;

    public Capability Capability => Capability.Supported;

    public bool Register(HotkeyGesture gesture, Action onPressed)
    {
        Unregister();
        if (!KeyCodes.TryGetValue(gesture.Key, out var code))
            return false;

        _onPressed = onPressed;
        _handler = (_, _, _) =>
        {
            _onPressed?.Invoke();
            return 0;
        };
        var spec = new[]
        {
            new Native.EventTypeSpec { EventClass = KeyboardClass, EventKind = HotKeyPressed },
        };
        var target = Native.GetApplicationEventTarget();

        if (
            Native.InstallEventHandler(target, _handler, 1, spec, IntPtr.Zero, out _handlerRef) != 0
        )
            return false;
        var id = new Native.EventHotKeyID { Signature = Signature, Id = 1 };
        if (
            Native.RegisterEventHotKey(
                code,
                Modifiers(gesture.Modifiers),
                id,
                target,
                0,
                out _hotKeyRef
            ) != 0
        )
        {
            Unregister();
            return false;
        }
        return true;
    }

    public void Unregister()
    {
        if (_hotKeyRef != IntPtr.Zero)
            Native.UnregisterEventHotKey(_hotKeyRef);
        if (_handlerRef != IntPtr.Zero)
            Native.RemoveEventHandler(_handlerRef);
        _hotKeyRef = _handlerRef = IntPtr.Zero;
    }

    public void Dispose() => Unregister();

    static uint Modifiers(HotkeyModifiers m) =>
        (m.HasFlag(HotkeyModifiers.Command) ? 0x100u : 0)
        | (m.HasFlag(HotkeyModifiers.Shift) ? 0x200u : 0)
        | (m.HasFlag(HotkeyModifiers.Alt) ? 0x800u : 0)
        | (m.HasFlag(HotkeyModifiers.Control) ? 0x1000u : 0);
}
