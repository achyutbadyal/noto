using Noto.Core.Models;
using Noto.Platform.Abstractions;

namespace Noto.App.Logic;

// Human words for the mode enums. One place, so the settings dropdowns, the command bar and the
// in-app guide all describe a mode the same way — "PriorityCarry" tells a new user nothing.
public static class ModeLabels
{
    public static string Of(Layout value) =>
        value switch
        {
            Layout.List => "List",
            Layout.Board => "Board",
            Layout.Timeline => "Timeline",
            Layout.HabitGrid => "Habit grid",
            _ => value.ToString(),
        };

    public static string Of(SortOrderMode value) =>
        value switch
        {
            SortOrderMode.Manual => "Manual",
            SortOrderMode.PriorityCarry => "Priority + carry",
            SortOrderMode.DueDate => "Due date",
            SortOrderMode.CarryDesc => "Carry",
            SortOrderMode.TimeOfDay => "Time of day",
            _ => value.ToString(),
        };

    public static string Of(Pressure value) =>
        value switch
        {
            Pressure.Gentle => "Gentle",
            Pressure.Honest => "Honest",
            Pressure.Relentless => "Relentless",
            _ => value.ToString(),
        };

    public static string Of(CapacityUnit value) =>
        value switch
        {
            CapacityUnit.Minutes => "Minutes",
            CapacityUnit.Items => "Items",
            _ => value.ToString(),
        };

    public static string Of(SecretStorageMode value) =>
        value switch
        {
            SecretStorageMode.MachineEncrypted => "This device only",
            SecretStorageMode.PassphraseEncrypted => "Master passphrase",
            SecretStorageMode.PlainFile => "Plain file",
            SecretStorageMode.OsKeyring => "System keychain",
            _ => value.ToString(),
        };

    // The trade-off, not the mechanism: what changes for the person picking it.
    public static string Describe(SecretStorageMode value) =>
        value switch
        {
            SecretStorageMode.MachineEncrypted =>
                "Sealed with a key made from this machine. Nothing to remember, and no keychain prompt after an update.",
            SecretStorageMode.PassphraseEncrypted =>
                "Sealed with a passphrase you type. Nothing opens without it, and the key is never written down.",
            SecretStorageMode.PlainFile =>
                "Unencrypted JSON in the app data folder. Anyone who can read your disk can read your tokens.",
            SecretStorageMode.OsKeyring =>
                "The system keychain. Your OS may ask for permission again each time the app is updated.",
            _ => "",
        };
}
