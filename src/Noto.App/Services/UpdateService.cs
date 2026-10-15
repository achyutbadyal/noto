using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Noto.Core.Updates;

namespace Noto.App.Services;

// A published GitHub release newer than the running app. AssetUrl is null when the release has no build for
// this OS/architecture; the user can still open PageUrl and download by hand.
public sealed record UpdateRelease(
    AppVersion Version,
    string Tag,
    string Notes,
    Uri PageUrl,
    Uri? AssetUrl,
    string? AssetName,
    Uri? ChecksumsUrl
);

// Swaps the running installation for a downloaded build and relaunches. Only platforms that can do this
// safely implement it; everywhere else the updater tells the user and opens the release page.
public interface IUpdateInstaller
{
    bool CanInstall { get; }

    // Stages and verifies the build, then starts a detached helper that waits for this process to exit,
    // replaces the install and relaunches. The caller must quit the app afterwards.
    Task PrepareAsync(string archivePath, CancellationToken ct);
}

// Finds new releases of Noto on GitHub and downloads them. Releases come from the unauthenticated releases
// API (public repo), and a download is only installed after its SHA-256 matches the release's SHA256SUMS.
public sealed class UpdateService(
    HttpClient http,
    AppVersion current,
    string repo = UpdateService.DefaultRepo,
    IUpdateInstaller? installer = null,
    string? assetName = null
)
{
    public const string DefaultRepo = "achyutbadyal/noto";
    public const string ChecksumsAsset = "SHA256SUMS";

    readonly string? _assetName = assetName ?? AssetNameFor();

    public AppVersion Current => current;
    public bool CanInstall => installer is { CanInstall: true };

    // The release asset built for this machine, or null when CI doesn't publish one. Names match the
    // artifacts in .github/workflows/release.yml.
    public static string? AssetNameFor() =>
        (OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), OperatingSystem.IsWindows()) switch
        {
            (true, _, _) when RuntimeInformation.OSArchitecture == Architecture.Arm64 =>
                "noto-macos-arm64.zip",
            (_, true, _) when RuntimeInformation.OSArchitecture == Architecture.X64 =>
                "noto-linux-x64.tar.gz",
            (_, _, true) when RuntimeInformation.OSArchitecture == Architecture.X64 =>
                "noto-windows-win-x64.zip",
            _ => null,
        };

    // Null when the running app is already the newest. Prereleases are only offered to someone already
    // running one.
    public async Task<UpdateRelease?> CheckAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repo}/releases?per_page=20"
        );
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Noto", current.ToString()));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        return Pick(await JsonDocument.ParseAsync(body, cancellationToken: ct));
    }

    UpdateRelease? Pick(JsonDocument releases)
    {
        UpdateRelease? best = null;
        foreach (var r in releases.RootElement.EnumerateArray())
        {
            if (r.GetProperty("draft").GetBoolean())
                continue;
            var tag = r.GetProperty("tag_name").GetString();
            if (!AppVersion.TryParse(tag, out var version))
                continue;
            if (version.IsPrerelease && !current.IsPrerelease)
                continue;
            if (version <= current || (best is not null && version <= best.Version))
                continue;

            Uri? asset = null,
                sums = null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString();
                var url = new Uri(a.GetProperty("browser_download_url").GetString()!);
                if (name == _assetName)
                    asset = url;
                else if (name == ChecksumsAsset)
                    sums = url;
            }
            best = new UpdateRelease(
                version,
                tag!,
                r.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "",
                new Uri(r.GetProperty("html_url").GetString()!),
                asset,
                asset is null ? null : _assetName,
                sums
            );
        }
        return best;
    }

    // Downloads, verifies and stages the update. Returns once the helper is waiting for this process to
    // exit; the caller then shuts the app down.
    public async Task InstallAsync(
        UpdateRelease release,
        IProgress<double>? progress = null,
        CancellationToken ct = default
    )
    {
        if (installer is not { CanInstall: true })
            throw new InvalidOperationException("This install can't update itself.");
        if (release.AssetUrl is null || release.ChecksumsUrl is null || release.AssetName is null)
            throw new InvalidOperationException(
                "This release has no verified build for this machine."
            );

        var dir = Directory.CreateTempSubdirectory("noto-update-").FullName;
        var file = Path.Combine(dir, release.AssetName);
        try
        {
            var expected = await ExpectedHashAsync(release, ct);
            var actual = await DownloadAsync(release.AssetUrl, file, progress, ct);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The download doesn't match the release checksum, so it was discarded."
                );
            await installer.PrepareAsync(file, ct);
        }
        finally
        {
            // The staged copy lives next to the install by now; the download itself is no longer needed.
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
        }
    }

    async Task<string> ExpectedHashAsync(UpdateRelease release, CancellationToken ct)
    {
        var text = await http.GetStringAsync(release.ChecksumsUrl, ct);
        return ParseChecksums(text, release.AssetName!)
            ?? throw new InvalidDataException(
                $"{ChecksumsAsset} has no entry for {release.AssetName}."
            );
    }

    // `sha256sum` format: "<hex>  <name>" (a "*" before the name marks binary mode).
    public static string? ParseChecksums(string text, string name)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*').Trim() == name)
                return parts[0];
        }
        return null;
    }

    async Task<string> DownloadAsync(
        Uri url,
        string path,
        IProgress<double>? progress,
        CancellationToken ct
    )
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(path);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            sha.AppendData(buffer, 0, n);
            await target.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total is > 0)
                progress?.Report((double)read / total.Value);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
