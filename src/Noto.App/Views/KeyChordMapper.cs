using Avalonia.Input;
using Noto.App.Logic;

namespace Noto.App.Views;

// Avalonia key events -> the normalized chords the view-models understand.
public static class KeyChordMapper
{
    public static KeyChord? From(KeyEventArgs e)
    {
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || (!OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Control));
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        // Symbols produced with Shift are matched as the symbol itself (e.g. "?" and "~"), with no Shift flag.
        string? key = e.Key switch
        {
            Key.Enter or Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Back => "Backspace",
            Key.Up => "ArrowUp",
            Key.Down => "ArrowDown",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemQuestion => shift ? Unshift("?", ref shift) : "/",
            Key.OemTilde => shift ? Unshift("~", ref shift) : "`",
            >= Key.A and <= Key.Z => e.Key.ToString().ToLowerInvariant(),
            >= Key.D0 and <= Key.D9 => ((int)e.Key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)e.Key - (int)Key.NumPad0).ToString(),
            _ => null,
        };
        return key is null ? null : new KeyChord(key, meta, shift, alt);
    }

    static string Unshift(string symbol, ref bool shift)
    {
        shift = false;
        return symbol;
    }
}
