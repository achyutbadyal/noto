using System.Diagnostics;
using System.Text.Json;
using Noto.Core.Ai;

namespace Noto.Providers.Ai;

// Apple Intelligence on Apple silicon, through a small Swift helper that wraps the FoundationModels
// framework (macOS 26+). The helper reads {system, user} as JSON on stdin and writes the model's reply
// on stdout, so nothing here needs Swift interop and nothing leaves the Mac.
//
// The helper is not bundled yet, so on a machine without it the mode reports itself unavailable and the
// settings pane says why rather than offering a mode that cannot run (docs/07 §19 — degrade visibly).
public sealed class AppleOnDeviceProvider : IAiProvider
{
    public const string HelperName = "noto-ai-helper";

    // The first call loads the model into memory (a few seconds); later calls are much faster. The bound
    // exists so a wedged helper can never leave the sparkle spinning forever.
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public IReadOnlyList<AiMode> Modes => [AiMode.AppleOnDevice];

    public string DisplayName => "Apple Intelligence (on device)";

    public string? UnavailableReason =>
        !OperatingSystem.IsMacOS()
            ? "On-device Apple Intelligence is only available on Apple devices."
        // FoundationModels ships with macOS 26 and the helper is built against it, so the binary would not
        // even load on an older system. Refuse the mode rather than launch it and fail.
        : !OperatingSystem.IsMacOSVersionAtLeast(26)
            ? "On-device Apple Intelligence needs macOS 26 or later."
        : Helper() is null
            // Name the directory that was searched. "Not installed" is the single most confusing state
            // here, because the binary is easy to build into a different output layout than the one the
            // running app was launched from.
            ? $"Not installed yet. Run `mise run ai:helper`, then Recheck. Looked in {AppContext.BaseDirectory} and on PATH."
        : null;

    public async Task<string> CompleteAsync(
        AiConnection connection,
        AiPrompt prompt,
        CancellationToken ct
    )
    {
        var helper =
            Helper()
            ?? throw new AiSuggestionException($"The {HelperName} helper isn't installed.");

        var startInfo = new ProcessStartInfo(helper)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(startInfo)
            ?? throw new AiSuggestionException("Couldn't start the Apple Intelligence helper.");

        // Bound the whole exchange. A caller-initiated cancel still surfaces as a cancel; only our own
        // deadline becomes a readable failure.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        var token = deadline.Token;

        try
        {
            await process.StandardInput.WriteAsync(RequestJson(prompt));
            process.StandardInput.Close();

            // Start stderr first so a chatty helper can't fill its pipe and block stdout.
            var error = process.StandardError.ReadToEndAsync(token);
            var output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);

            if (process.ExitCode != 0)
            {
                var reason = (await error).Trim();
                throw new AiSuggestionException(
                    reason.Length > 0 ? reason : "Apple Intelligence didn't answer."
                );
            }
            return output;
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new AiSuggestionException("Apple Intelligence took too long to answer.", e);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    // The bridge contract: the Swift helper (tools/noto-ai-helper) decodes exactly these two keys from
    // stdin and writes the reply to stdout. Pinned by a test, because nothing else would catch a rename
    // here breaking the helper silently.
    public static string RequestJson(AiPrompt prompt) =>
        JsonSerializer.Serialize(new { system = prompt.System, user = prompt.User });

    // Next to the app first (the shipping layout), then on PATH (a developer build).
    static string? Helper()
    {
        var local = Path.Combine(AppContext.BaseDirectory, HelperName);
        if (File.Exists(local))
            return local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (
            var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var candidate = Path.Combine(directory, HelperName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            // The process already went away; nothing to clean up.
        }
    }
}
