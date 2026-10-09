using System.Diagnostics;
using Noto.Platform.Abstractions;
using Noto.Platform.Internal;
using Noto.Platform.Linux;

namespace Noto.Platform.Tests;

public class LinuxPlatformTests
{
    [Theory]
    [InlineData("Space", "space")]
    [InlineData("A", "a")]
    [InlineData("5", "5")]
    [InlineData("f3", "F3")]
    public void Keys_map_to_keysym_names(string key, string expected) =>
        X11Keys.KeysymName(key).ShouldBe(expected);

    [Theory]
    [InlineData("F13")]
    [InlineData("Enter")]
    public void Unknown_keys_have_no_keysym(string key) => X11Keys.KeysymName(key).ShouldBeNull();

    [Fact]
    public void Modifiers_use_x11_masks_and_super_for_command()
    {
        X11Keys.Modifiers(HotkeyGesture.DefaultCapture.Modifiers).ShouldBe(4u | 8u); // ControlMask | Mod1Mask
        X11Keys.Modifiers(HotkeyModifiers.Command | HotkeyModifiers.Shift).ShouldBe(64u | 1u);
    }

    [Fact]
    public void Grabs_cover_caps_lock_and_num_lock()
    {
        var variants = X11Keys.WithLockVariants(4u | 8u);
        variants.ShouldBe([12u, 14u, 28u, 30u], ignoreOrder: true);
    }

    [Fact]
    public void Notify_send_arguments_cannot_be_read_as_options()
    {
        var args = NotifySendNotifications.Arguments(new("--urgency=critical", "-b"));
        args.ShouldBe(["--app-name=Noto", "--", "--urgency=critical", "-b"]);
    }

    [Fact]
    public void Polled_reduce_motion_reports_changes_and_survives_failed_reads()
    {
        var value = false;
        var fail = false;
        using var motion = new PolledReduceMotion(
            () => fail ? throw new IOException() : value,
            TimeSpan.FromMilliseconds(20)
        );
        using var changed = new ManualResetEventSlim();
        motion.Changed += changed.Set;

        motion.IsEnabled.ShouldBeFalse();
        fail = true;
        Thread.Sleep(100);
        motion.IsEnabled.ShouldBeFalse(); // a failed read keeps the last value

        fail = false;
        value = true;
        changed.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue();
        motion.IsEnabled.ShouldBeTrue();
    }

    // A stand-in for secret-tool: keeps one secret in a file, like the real one keyed by its attributes.
    sealed class FakeSecretTool : IDisposable
    {
        readonly string _dir = Directory.CreateTempSubdirectory("noto-secret-tool").FullName;

        public string Path => System.IO.Path.Combine(_dir, "secret-tool");
        public string ArgsLog => System.IO.Path.Combine(_dir, "args.log");

        public FakeSecretTool()
        {
            File.WriteAllText(
                Path,
                $$"""
                #!/bin/sh
                echo "$@" >> "{{_dir}}/args.log"
                case "$1" in
                  store) cat > "{{_dir}}/store.$4.$6" ;;
                  lookup) if [ -f "{{_dir}}/store.$3.$5" ]; then cat "{{_dir}}/store.$3.$5"; else exit 1; fi ;;
                  clear) rm -f "{{_dir}}/store.$3.$5" ;;
                esac
                """
            );
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    Path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Secret_tool_keyring_sends_the_secret_on_stdin_not_argv()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var tool = new FakeSecretTool();
        var keyring = new SecretToolKeyring(tool.Path);

        (await keyring.GetAsync("svc", "acct")).ShouldBeNull(); // exit 1, no stderr: not found
        await keyring.SetAsync("svc", "acct", "s3cr3t ✓");
        (await keyring.GetAsync("svc", "acct")).ShouldBe("s3cr3t ✓");
        await keyring.DeleteAsync("svc", "acct");
        (await keyring.GetAsync("svc", "acct")).ShouldBeNull();

        File.ReadAllText(tool.ArgsLog).ShouldNotContain("s3cr3t");
    }

    [Fact]
    public async Task A_missing_helper_program_is_reported_not_thrown()
    {
        var result = await ProcessRunner.RunAsync("/definitely/not/here", []);
        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldContain("Couldn't start");
    }

    [Fact]
    public async Task A_helper_that_never_reads_stdin_is_not_a_runner_failure()
    {
        if (OperatingSystem.IsWindows())
            return; // the stand-in below is a shell script

        using var tool = new FakeSecretTool();
        // Exits without reading stdin, so its end of the pipe closes while the secret is still being
        // written. A payload bigger than the pipe buffer guarantees the write outlives the child.
        File.WriteAllText(tool.Path, "#!/bin/sh\nexit 2\n");
        var keyring = new SecretToolKeyring(tool.Path);

        var e = await Should.ThrowAsync<InvalidOperationException>(() =>
            keyring.SetAsync("svc", "acct", new string('x', 1 << 20))
        );
        e.Message.ShouldContain("secret-tool store failed");
    }

    [Fact]
    public async Task Secret_tool_keyring_surfaces_helper_failures()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var tool = new FakeSecretTool();
        File.WriteAllText(tool.Path, "#!/bin/sh\necho 'no secret service' >&2\nexit 2\n");
        var keyring = new SecretToolKeyring(tool.Path);

        var e = await Should.ThrowAsync<InvalidOperationException>(() =>
            keyring.SetAsync("svc", "acct", "x")
        );
        e.Message.ShouldContain("no secret service");
    }

    [Fact]
    public void Hotkey_explains_why_it_is_unavailable_under_wayland()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
            using var hotkey = new X11Hotkey();
            hotkey.Capability.IsSupported.ShouldBeFalse();
            hotkey.Capability.Reason!.ShouldContain("noto --capture");
            hotkey.Register(HotkeyGesture.DefaultCapture, () => { }).ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", session);
        }
    }
}
