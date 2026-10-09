using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Links;
using Noto.Platform.Abstractions;
using Noto.Sync;

namespace Noto.App.Tests;

// The connect form: which fields each provider needs, and that a failing connect is reported, not thrown.
public sealed class AccountViewModelTests
{
    static readonly TokenOption Jira = new(
        "jira",
        "Jira",
        AuthMethod.ApiKey,
        SiteField.Required,
        true
    );
    static readonly TokenOption GitHub = new(
        "github",
        "GitHub",
        AuthMethod.PersonalToken,
        SiteField.Optional,
        false
    );

    static AccountViewModel Build(params TokenOption[] options)
    {
        var account = new AccountService(
            new NoAuth(),
            new InMemoryKeyring(),
            new InMemoryUiState(),
            new ServerDevice(Guid.NewGuid(), "test", "macos"),
            null!,
            null!,
            options,
            TimeProvider.System
        );
        return new AccountViewModel(account);
    }

    [Fact]
    public void Jira_needs_site_email_and_token_before_connect_is_enabled()
    {
        var vm = Build(Jira);
        vm.SelectedTokenOption.ShouldBe(Jira);
        vm.ShowSite.ShouldBeTrue();
        vm.ShowSiteRequired.ShouldBeTrue();
        vm.ShowEmail.ShouldBeTrue();

        vm.Token = "api-token";
        vm.CanConnect.ShouldBeFalse();
        vm.ConnectSite = "https://acme.atlassian.net";
        vm.CanConnect.ShouldBeFalse();
        vm.ConnectEmail = "dana@acme.com";
        vm.CanConnect.ShouldBeTrue();
    }

    [Fact]
    public void GitHub_needs_only_a_token_and_the_site_is_optional()
    {
        var vm = Build(GitHub);
        vm.ShowSite.ShouldBeTrue();
        vm.ShowSiteRequired.ShouldBeFalse();
        vm.ShowEmail.ShouldBeFalse();

        vm.Token = "ghp_token";
        vm.CanConnect.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_connect_is_reported_not_thrown()
    {
        var vm = Build(GitHub);
        vm.Token = "ghp_token";

        // No connection service is wired in this test, so the connect call fails.
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.ConnectionStatusIsError.ShouldBeTrue();
        vm.HasConnectionStatus.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_providers_the_signed_in_server_runs_are_offered_for_browser_sign_in()
    {
        var account = await SignedInAccount.CreateAsync(
            new InMemoryUiState(),
            [
                new OAuthOption("linear", "Linear", Configured: false, "setup"),
                new OAuthOption("github", "GitHub", Configured: false, "setup"),
            ],
            serverProviders: ["github"]
        );
        var vm = new AccountViewModel(account);

        await vm.LoadAsync();

        vm.OAuthOptions.Select(o => o.ProviderId).ShouldBe(new[] { "github" });
        vm.SelectedOAuthOption!.ProviderId.ShouldBe("github");
        vm.HasOAuthOptions.ShouldBeTrue();
        vm.OAuthSetupHint.ShouldBeEmpty();
        vm.ConnectOAuthCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task A_signed_in_server_with_no_provider_apps_says_so_and_offers_nothing()
    {
        var account = await SignedInAccount.CreateAsync(
            new InMemoryUiState(),
            [new OAuthOption("github", "GitHub", Configured: false, "setup")],
            serverProviders: []
        );
        var vm = new AccountViewModel(account);

        await vm.LoadAsync();

        vm.HasOAuthOptions.ShouldBeFalse();
        vm.OAuthSetupHint.ShouldContain("no sign-in apps set up");
        vm.ConnectOAuthCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Without_a_server_session_the_hint_asks_to_connect_the_device()
    {
        var account = await SignedInAccount.CreateAsync(
            new InMemoryUiState(),
            [new OAuthOption("github", "GitHub", Configured: false, "setup")],
            serverProviders: ["github"],
            signedIn: false
        );
        var vm = new AccountViewModel(account);

        await vm.LoadAsync();

        vm.HasOAuthOptions.ShouldBeFalse();
        vm.OAuthSetupHint.ShouldContain("Connect this device to it");
    }

    sealed class NoAuth : IServerAuth
    {
        public Task<ServerSession> SignUpAsync(
            Uri s,
            string e,
            string p,
            string? i,
            ServerDevice d,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ServerSession> SignInAsync(
            Uri s,
            string e,
            string p,
            ServerDevice d,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ServerSession> RefreshAsync(Uri s, string r, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SignOutAsync(Uri s, string a, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
