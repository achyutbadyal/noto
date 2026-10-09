using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Interfaces;
using Noto.Core.Links;
using Noto.Platform.Abstractions;
using Noto.Providers;
using Noto.Providers.Providers;
using Noto.Sync;

namespace Noto.App.Tests.Ui;

// Regressions for the desktop crash when opening Settings. The desktop build builds its account from the real
// provider list, with no server set and no OAuth apps configured, so these mirror that setup.
public sealed class SettingsRegressionTests : IDisposable
{
    readonly AppFixture _app = new();

    public void Dispose() => _app.Dispose();

    [AvaloniaFact]
    public async Task Settings_opens_with_the_desktop_account_setup()
    {
        var registry = new ProviderRegistry([
            new FigmaProvider(),
            new GitHubProvider(),
            new GitLabProvider(),
            new JiraProvider(),
            new ConfluenceProvider(),
            new LinearProvider(),
            new NotionProvider(),
            new SlackProvider(),
            new OpenGraphProvider(),
        ]);
        var tokens = registry
            .Providers.SelectMany(p =>
                p.SupportedAuthMethods.Where(m =>
                        m is AuthMethod.PersonalToken or AuthMethod.ApiKey
                    )
                    .Select(m => new TokenOption(
                        p.ProviderId,
                        p.DisplayName,
                        m,
                        p.RequiresInstanceUrl ? SiteField.Required
                            : p.AcceptsSiteAddress ? SiteField.Optional
                            : SiteField.None,
                        p.UsesUsername(m)
                    ))
            )
            .OrderBy(o => o.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Method)
            .ToList();
        var oauth = registry
            .Providers.Where(p => p.SupportedAuthMethods.Contains(AuthMethod.OAuth2))
            .Select(p => new OAuthOption(
                p.ProviderId,
                p.DisplayName,
                Configured: false,
                "setup",
                p.OAuthAcceptsSite
            ))
            .ToList();
        var account = new AccountService(
            new UnusedServerAuth(),
            new InMemoryKeyring(),
            _app.Services.UiState,
            new ServerDevice(Guid.NewGuid(), "Test", "macos"),
            _app.Services.Uow,
            null!,
            tokens,
            TimeProvider.System,
            oauth,
            _ => Task.CompletedTask
        );
        var shell = new ShellViewModel(_app.Services, account);

        await shell.InitializeAsync();
        await shell.GoAsync(AppPage.Settings);
        Dispatcher.UIThread.RunJobs();

        shell.Settings.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_failed_connection_load_is_shown_not_thrown()
    {
        var account = new AccountService(
            new UnusedServerAuth(),
            new InMemoryKeyring(),
            new InMemoryUiState(),
            new ServerDevice(Guid.NewGuid(), "Test", "macos"),
            new FailingStore(),
            null!,
            [],
            TimeProvider.System
        );
        var vm = new AccountViewModel(account);

        await vm.LoadAsync();

        vm.ConnectionStatusIsError.ShouldBeTrue();
        vm.ConnectionStatus!.ShouldContain("no such column");
    }

    // Every database call fails the way a schema mismatch does.
    sealed class FailingStore : IUnitOfWork
    {
        public Task<T> RunAsync<T>(Func<IStore, Task<T>> work) =>
            throw new InvalidOperationException("no such column: api_base_url");
    }

    sealed class UnusedServerAuth : IServerAuth
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
