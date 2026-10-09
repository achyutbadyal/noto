using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Noto.Platform.Internal;

internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;

    // The program is missing or could not start.
    public static ProcessResult NotRun(string reason) => new(-1, "", reason);
}

// Runs a helper program without a shell: arguments are passed as a list, and secrets go through stdin,
// never through argv where other users could read them from the process table.
internal static class ProcessRunner
{
    static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static async Task<ProcessResult> RunAsync(
        string file,
        IEnumerable<string> args,
        string? stdin = null,
        TimeSpan? timeout = null
    )
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (stdin is not null)
            info.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var a in args)
            info.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("no process");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return ProcessResult.NotRun($"Couldn't start {file}: {e.Message}");
        }

        using (process)
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return ProcessResult.NotRun($"{file} did not finish in time.");
            }
            return new ProcessResult(process.ExitCode, await output, await error);
        }
    }

    public static string? FindOnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };
        foreach (
            var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        )
        {
            if (dir.Length == 0)
                continue;
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
