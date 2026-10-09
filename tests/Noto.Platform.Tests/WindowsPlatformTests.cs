using System.Text;
using Noto.Platform.Abstractions;
using Noto.Platform.Windows;

namespace Noto.Platform.Tests;

public class WindowsPlatformTests
{
    [Theory]
    [InlineData("Space", 0x20u)]
    [InlineData("a", 0x41u)]
    [InlineData("Z", 0x5Au)]
    [InlineData("7", 0x37u)]
    [InlineData("F1", 0x70u)]
    [InlineData("f12", 0x7Bu)]
    public void Keys_map_to_virtual_key_codes(string key, uint expected)
    {
        WindowsKeys.TryGetVirtualKey(key, out var vk).ShouldBeTrue();
        vk.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F13")]
    [InlineData("Enter")]
    [InlineData("é")]
    public void Unknown_keys_are_rejected(string key) =>
        WindowsKeys.TryGetVirtualKey(key, out _).ShouldBeFalse();

    [Fact]
    public void Default_capture_gesture_is_ctrl_alt_space_without_auto_repeat()
    {
        var mods = WindowsKeys.Modifiers(HotkeyGesture.DefaultCapture.Modifiers);
        mods.ShouldBe(0x1u | 0x2u | 0x4000u); // MOD_ALT | MOD_CONTROL | MOD_NOREPEAT
    }

    [Fact]
    public void Command_maps_to_the_windows_key()
    {
        (WindowsKeys.Modifiers(HotkeyModifiers.Command) & 0x8u).ShouldBe(0x8u);
        (WindowsKeys.Modifiers(HotkeyModifiers.Shift) & 0x4u).ShouldBe(0x4u);
    }

    [Fact]
    public void Toast_script_escapes_xml_and_powershell_quotes()
    {
        var script = ToastScript.Build(
            new NotificationRequest("Don't <panic>", "a & b '; Remove-Item x; '")
        );

        // XML escaping already turns ' into &apos;, so no raw quote can end the PowerShell string early.
        script.ShouldContain("Don&apos;t &lt;panic&gt;");
        script.ShouldContain("a &amp; b &apos;; Remove-Item x; &apos;");
        script.ShouldNotContain("b '");
        script.ShouldContain("CreateToastNotifier('app.noto')");
        Encoding
            .Unicode.GetString(
                Convert.FromBase64String(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            )
            .ShouldBe(script);
    }

    [Fact]
    public async Task Credential_manager_stores_updates_reads_and_deletes_a_secret()
    {
        if (!OperatingSystem.IsWindows())
            return; // verified on Windows only (CI runs this on windows-latest)

        var keyring = new WinKeyring();
        var service = $"app.noto.tests.{Guid.NewGuid():N}";
        try
        {
            (await keyring.GetAsync(service, "conn-1")).ShouldBeNull();
            await keyring.SetAsync(service, "conn-1", "token-one");
            (await keyring.GetAsync(service, "conn-1")).ShouldBe("token-one");
            await keyring.SetAsync(service, "conn-1", "tökén-two ✓");
            (await keyring.GetAsync(service, "conn-1")).ShouldBe("tökén-two ✓");
        }
        finally
        {
            await keyring.DeleteAsync(service, "conn-1");
            await keyring.DeleteAsync(service, "conn-1"); // deleting a missing item is fine
        }
        (await keyring.GetAsync(service, "conn-1")).ShouldBeNull();
    }

    [Fact]
    public void Hotkey_registers_and_releases_the_chord()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var first = new WinHotkey();
        var gesture = new HotkeyGesture("F9", HotkeyModifiers.Control | HotkeyModifiers.Shift);
        first.Register(gesture, () => { }).ShouldBeTrue();

        using (var second = new WinHotkey())
            second.Register(gesture, () => { }).ShouldBeFalse(); // already owned

        first.Unregister();
        using var third = new WinHotkey();
        third.Register(gesture, () => { }).ShouldBeTrue(); // released
    }
}
