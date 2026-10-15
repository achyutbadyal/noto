using System.Diagnostics;

namespace Noto.App.Services;

// Replaces Noto.app in place. The new bundle is unpacked beside the old one (same volume, so the final swap
// is a rename), checked with codesign, and a detached shell script performs the swap once this process has
// exited and then relaunches. A build we downloaded ourselves carries no quarantine flag, so Gatekeeper's
// "unidentified developer" prompt doesn't reappear for updates after the first launch.
public sealed class MacBundleInstaller : IUpdateInstaller
{
    const string Script = """
        #!/bin/sh
        # usage: apply.sh <pid> <installed bundle> <new bundle> <staging dir>
        while kill -0 "$1" 2>/dev/null; do sleep 0.2; done
        backup="$2.old"
        rm -rf "$backup"
        mv "$2" "$backup" || exit 1
        if mv "$3" "$2"; then
          xattr -dr com.apple.quarantine "$2" 2>/dev/null
          rm -rf "$backup"
        else
          mv "$backup" "$2"
        fi
        open "$2"
        rm -rf "$4"
        """;

    readonly string? _bundle = FindBundle(AppContext.BaseDirectory);

    public bool CanInstall =>
        OperatingSystem.IsMacOS()
        && _bundle is not null
        && CanWrite(Path.GetDirectoryName(_bundle)!);

    // The .app that contains this executable, or null when running from `dotnet run` or a plain folder.
    public static string? FindBundle(string baseDirectory)
    {
        var dir = new DirectoryInfo(baseDirectory);
        while (dir is not null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.Ordinal))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".noto-write-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task PrepareAsync(string archivePath, CancellationToken ct)
    {
        var bundle =
            _bundle ?? throw new InvalidOperationException("Not running from an app bundle.");
        var stage = Path.Combine(
            Path.GetDirectoryName(bundle)!,
            $".Noto-update-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(stage);
        try
        {
            await RunAsync("/usr/bin/ditto", ["-x", "-k", archivePath, stage], ct);
            var fresh = Path.Combine(stage, Path.GetFileName(bundle));
            if (!Directory.Exists(fresh))
                throw new InvalidDataException("The download doesn't contain a Noto.app.");
            await RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", fresh], ct);

            var script = Path.Combine(stage, "apply.sh");
            await File.WriteAllTextAsync(script, Script, ct);
            Launch(script, bundle, fresh, stage);
        }
        catch
        {
            Directory.Delete(stage, recursive: true);
            throw;
        }
    }

    // Backgrounded through `sh -c` so the helper outlives this process.
    static void Launch(string script, string bundle, string fresh, string stage)
    {
        var info = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("nohup /bin/sh \"$@\" >/dev/null 2>&1 &");
        info.ArgumentList.Add("noto-update");
        info.ArgumentList.Add(script);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(bundle);
        info.ArgumentList.Add(fresh);
        info.ArgumentList.Add(stage);
        Process.Start(info)?.Dispose();
    }

    static async Task RunAsync(string tool, string[] args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(tool)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new IOException($"Couldn't start {tool}.");
        var error = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(tool)} failed: {error.Trim()}");
    }
}
