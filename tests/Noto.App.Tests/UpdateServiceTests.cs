using System.Net;
using System.Text;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Updates;

namespace Noto.App.Tests;

public class UpdateServiceTests
{
    const string Asset = "noto-macos-arm64.zip";

    static string Release(
        string tag,
        bool prerelease = false,
        bool draft = false,
        bool assets = true
    ) =>
        $$"""
        {"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},
         "body":"notes for {{tag}}","html_url":"https://github.com/o/r/releases/tag/{{tag}}",
         "assets":[{{(assets
            ? $$"""
              {"name":"{{Asset}}","browser_download_url":"https://dl/{{tag}}/{{Asset}}"},
              {"name":"SHA256SUMS","browser_download_url":"https://dl/{{tag}}/SHA256SUMS"}
              """
            : "")}}]}
        """;

    static UpdateService Service(string current, params string[] releases) =>
        new(
            new HttpClient(new Stub(_ => Json("[" + string.Join(",", releases) + "]"))),
            AppVersion.Parse(current),
            "o/r",
            assetName: Asset
        );

    static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r,
            CancellationToken ct
        ) => Task.FromResult(respond(r));
    }

    [Fact]
    public async Task Offers_the_newest_release_with_this_machines_asset()
    {
        var found = await Service("0.1.0", Release("v0.2.0"), Release("v0.3.0"), Release("v0.1.5"))
            .CheckAsync();

        found.ShouldNotBeNull();
        found.Version.ShouldBe(new AppVersion(0, 3, 0));
        found.AssetUrl.ShouldBe(new Uri($"https://dl/v0.3.0/{Asset}"));
        found.ChecksumsUrl.ShouldBe(new Uri("https://dl/v0.3.0/SHA256SUMS"));
        found.Notes.ShouldBe("notes for v0.3.0");
    }

    [Fact]
    public async Task Offers_nothing_when_up_to_date_or_ahead() =>
        (await Service("0.3.0", Release("v0.3.0"), Release("v0.2.0")).CheckAsync()).ShouldBeNull();

    [Fact]
    public async Task Skips_drafts_and_unparseable_tags() =>
        (
            await Service("0.1.0", Release("v9.0.0", draft: true), Release("nightly")).CheckAsync()
        ).ShouldBeNull();

    [Fact]
    public async Task Prereleases_only_reach_people_already_on_one()
    {
        var pre = Release("v0.2.0-beta.1", prerelease: true);
        (await Service("0.1.0", pre).CheckAsync()).ShouldBeNull();
        (await Service("0.2.0-alpha.1", pre).CheckAsync())!.Version.Prerelease.ShouldBe("beta.1");
    }

    [Fact]
    public async Task A_release_without_our_asset_still_surfaces_with_no_download()
    {
        var found = await Service("0.1.0", Release("v0.2.0", assets: false)).CheckAsync();
        found.ShouldNotBeNull();
        found.AssetUrl.ShouldBeNull();
    }

    [Fact]
    public void Reads_sha256sum_output()
    {
        var sums = "aaa111  noto-linux-x64.tar.gz\nBBB222 *noto-macos-arm64.zip\n";
        UpdateService.ParseChecksums(sums, "noto-macos-arm64.zip").ShouldBe("BBB222");
        UpdateService.ParseChecksums(sums, "missing.zip").ShouldBeNull();
    }

    sealed class RecordingInstaller : IUpdateInstaller
    {
        public bool CanInstall => true;
        public string? Prepared;

        public Task PrepareAsync(string archivePath, CancellationToken ct)
        {
            Prepared = archivePath;
            return Task.CompletedTask;
        }
    }

    static UpdateService Downloading(string payload, string sums, RecordingInstaller installer) =>
        new(
            new HttpClient(
                new Stub(r =>
                    r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS")
                        ? new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(sums),
                        }
                        : new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(payload),
                        }
                )
            ),
            new AppVersion(0, 1, 0),
            "o/r",
            installer,
            Asset
        );

    static UpdateRelease Found() =>
        new(
            new AppVersion(0, 2, 0),
            "v0.2.0",
            "",
            new Uri("https://github.com/o/r/releases/tag/v0.2.0"),
            new Uri($"https://dl/v0.2.0/{Asset}"),
            Asset,
            new Uri("https://dl/v0.2.0/SHA256SUMS")
        );

    [Fact]
    public async Task Installs_only_a_download_that_matches_its_checksum()
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("payload"u8));
        var installer = new RecordingInstaller();

        await Downloading("payload", $"{hash.ToLowerInvariant()}  {Asset}", installer)
            .InstallAsync(Found());

        installer.Prepared.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refuses_a_download_that_does_not_match()
    {
        var installer = new RecordingInstaller();

        await Should.ThrowAsync<InvalidDataException>(() =>
            Downloading("tampered", $"{new string('0', 64)}  {Asset}", installer)
                .InstallAsync(Found())
        );

        installer.Prepared.ShouldBeNull();
    }

    [Fact]
    public async Task The_view_model_reports_state_and_remembers_the_auto_check_choice()
    {
        var state = new InMemoryUiState();
        var vm = new UpdatesViewModel(Service("0.1.0", Release("v0.2.0")), state, _ => { });

        vm.AutoCheck.ShouldBeTrue();
        vm.AutoCheck = false;
        state.Get(UpdatesViewModel.AutoCheckKey).ShouldBe("off");

        await vm.CheckAsync();
        vm.HasUpdate.ShouldBeTrue();
        vm.Status.ShouldBe("Version 0.2.0 is available.");
        vm.CanInstall.ShouldBeFalse(); // no installer on this service
        vm.CanOpenPage.ShouldBeTrue();
    }
}
