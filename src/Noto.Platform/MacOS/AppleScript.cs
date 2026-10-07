using System.Diagnostics;
using System.Runtime.Versioning;

namespace Noto.Platform.MacOS;

[SupportedOSPlatform("macos")]
static class AppleScript
{
    // Returns trimmed stdout, or null if the script failed (e.g. Automation permission denied).
    public static async Task<string?> RunAsync(string script, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("/usr/bin/osascript") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return null;
        }
    }

    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
