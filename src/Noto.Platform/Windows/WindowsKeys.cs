using Noto.Platform.Abstractions;

namespace Noto.Platform.Windows;

// Win32 RegisterHotKey vocabulary: virtual-key codes and MOD_* flags.
static class WindowsKeys
{
    const uint ModAlt = 0x1;
    const uint ModControl = 0x2;
    const uint ModShift = 0x4;
    const uint ModWin = 0x8;
    const uint ModNoRepeat = 0x4000;

    public static bool TryGetVirtualKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
            vk = 0x20;
        else if (key.Length == 1 && char.IsAsciiLetter(key[0]))
            vk = char.ToUpperInvariant(key[0]);
        else if (key.Length == 1 && char.IsAsciiDigit(key[0]))
            vk = key[0];
        else if (
            key.Length is 2 or 3
            && (key[0] is 'F' or 'f')
            && int.TryParse(key.AsSpan(1), out var n)
            && n is >= 1 and <= 12
        )
            vk = (uint)(0x70 + n - 1);
        return vk != 0;
    }

    // Command maps to the Windows key. Auto-repeat is suppressed so holding the chord opens capture once.
    public static uint Modifiers(HotkeyModifiers m) =>
        ModNoRepeat
        | (m.HasFlag(HotkeyModifiers.Alt) ? ModAlt : 0)
        | (m.HasFlag(HotkeyModifiers.Control) ? ModControl : 0)
        | (m.HasFlag(HotkeyModifiers.Shift) ? ModShift : 0)
        | (m.HasFlag(HotkeyModifiers.Command) ? ModWin : 0);
}
