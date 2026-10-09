using Noto.Platform.Abstractions;

namespace Noto.Platform.Linux;

static class X11Keys
{
    const uint ShiftMask = 1;
    const uint LockMask = 2; // Caps Lock
    const uint ControlMask = 4;
    const uint Mod1Mask = 8; // Alt
    const uint Mod2Mask = 16; // Num Lock
    const uint Mod4Mask = 64; // Super

    // The keysym name Xlib's XStringToKeysym knows. Null for keys Noto doesn't offer.
    public static string? KeysymName(string key)
    {
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
            return "space";
        if (key.Length == 1 && char.IsAsciiLetter(key[0]))
            return key.ToLowerInvariant();
        if (key.Length == 1 && char.IsAsciiDigit(key[0]))
            return key;
        if (
            key.Length is 2 or 3
            && (key[0] is 'F' or 'f')
            && int.TryParse(key.AsSpan(1), out var n)
            && n is >= 1 and <= 12
        )
            return $"F{n}";
        return null;
    }

    public static uint Modifiers(HotkeyModifiers m) =>
        (m.HasFlag(HotkeyModifiers.Shift) ? ShiftMask : 0)
        | (m.HasFlag(HotkeyModifiers.Control) ? ControlMask : 0)
        | (m.HasFlag(HotkeyModifiers.Alt) ? Mod1Mask : 0)
        | (m.HasFlag(HotkeyModifiers.Command) ? Mod4Mask : 0);

    // X matches modifiers exactly, so a grab made without Num Lock / Caps Lock misses once either is on.
    // Grab every combination of those two lock bits.
    public static IReadOnlyList<uint> WithLockVariants(uint modifiers) =>
        [modifiers, modifiers | LockMask, modifiers | Mod2Mask, modifiers | LockMask | Mod2Mask];
}
