using Noto.Platform;
using Noto.Platform.Abstractions;
using Noto.Platform.MacOS;

namespace Noto.Platform.Tests;

public class PlatformTests
{
    [Fact]
    public async Task Unsupported_features_explain_themselves_instead_of_disappearing()
    {
        var hotkey = new UnsupportedHotkey("bind a shortcut to `noto --capture`");
        hotkey.Capability.IsSupported.ShouldBeFalse();
        hotkey.Capability.Reason!.ShouldContain("noto --capture");
        hotkey.Register(HotkeyGesture.DefaultCapture, () => { }).ShouldBeFalse();

        await new UnsupportedNotifications("nope").ShowAsync(new("t", "b"));
        (await new UnsupportedCaptureContext("nope").GetAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task In_memory_keyring_round_trips_and_says_it_is_not_secure()
    {
        var keyring = new InMemoryKeyring();
        keyring.Capability.IsSupported.ShouldBeFalse();

        await keyring.SetAsync("svc", "acct", "secret");
        (await keyring.GetAsync("svc", "acct")).ShouldBe("secret");
        await keyring.DeleteAsync("svc", "acct");
        (await keyring.GetAsync("svc", "acct")).ShouldBeNull();
    }

    [Fact]
    public void Default_capture_gesture_is_control_option_space()
    {
        HotkeyGesture.DefaultCapture.Key.ShouldBe("Space");
        HotkeyGesture.DefaultCapture.Modifiers.ShouldBe(HotkeyModifiers.Control | HotkeyModifiers.Alt);
    }

    [Fact]
    public void Factory_picks_a_platform_set_for_this_os()
    {
        using var services = PlatformFactory.Create().Hotkey;
        services.Capability.IsSupported.ShouldBe(OperatingSystem.IsMacOS());
    }

    [Fact]
    public async Task Mac_keychain_stores_updates_reads_and_deletes_a_secret()
    {
        if (!OperatingSystem.IsMacOS()) return; // verified on macOS only

        var keyring = new MacKeyring();
        var service = $"app.noto.tests.{Guid.NewGuid():N}";
        try
        {
            (await keyring.GetAsync(service, "conn-1")).ShouldBeNull();
            await keyring.SetAsync(service, "conn-1", "token-one");
            (await keyring.GetAsync(service, "conn-1")).ShouldBe("token-one");

            await keyring.SetAsync(service, "conn-1", "tökén-two ✓");     // update in place, UTF-8
            (await keyring.GetAsync(service, "conn-1")).ShouldBe("tökén-two ✓");
        }
        finally
        {
            await keyring.DeleteAsync(service, "conn-1");
        }
        (await keyring.GetAsync(service, "conn-1")).ShouldBeNull();
        await keyring.DeleteAsync(service, "conn-1"); // deleting a missing entry is a no-op
    }

    [Fact]
    public void Mac_reduce_motion_reads_the_os_flag_without_throwing()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var motion = new MacReduceMotion();
        _ = motion.IsEnabled; // value depends on the user's accessibility settings
    }

    [Fact]
    public void Mac_hotkey_rejects_unknown_keys_without_registering()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var hotkey = new MacHotkey();
        hotkey.Register(new HotkeyGesture("NotAKey", HotkeyModifiers.Control), () => { }).ShouldBeFalse();
    }
}
