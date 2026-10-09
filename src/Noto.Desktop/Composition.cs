using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Commands;
using Noto.Core.Links;
using Noto.Core.Time;
using Noto.Data;
using Noto.Platform;
using Noto.Platform.Abstractions;
using Noto.Providers;
using Noto.Providers.Auth;
using Noto.Providers.Preview;
using Noto.Providers.Providers;
using Noto.Providers.Transport;
using Noto.Sync;

namespace Noto.Desktop;

// Wires storage, platform services, provider access and the app services. A local install has no account,
// only a random device id.
sealed class Composition : IDisposable
{
    readonly SqliteUnitOfWork _db;

    public Composition(string? dataDirectory = null)
    {
        var dir = dataDirectory ?? DefaultDataDirectory();
        Directory.CreateDirectory(dir);

        _db = new SqliteUnitOfWork($"Data Source={Path.Combine(dir, "noto.db")}");
        var clock = new SystemClock();
        var deviceId = DeviceId(dir);
        var bus = new CommandBus(_db, clock, deviceId);

        Platform = PlatformFactory.Create();
        var ui = new FileUiState(Path.Combine(dir, "ui-state.json"));

        // One provider stack, shared by connecting, token handling and link previews.
        var http = new HttpClient();
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
        var credentials = new CredentialStore(new KeyringAdapter(Platform.Keyring));
        var transports = new DirectTransportFactory(http, NullLogger<DirectTransport>.Instance);
        // Provider sign-in runs on the Noto server, which holds the provider secrets. The session is the
        // account's, so the account is read when a request is made, not when the gateway is built.
        var gateway = new ServerOAuthGateway(
            http,
            ct =>
                (
                    Account ?? throw new InvalidOperationException("Account is not ready")
                ).SessionAsync(ct)
        );
        var tokens = new TokenManager(_db, credentials, registry, clock, gateway);
        var connections = new ConnectionService(
            _db,
            credentials,
            registry,
            transports,
            clock,
            gateway
        );
        var previews = new PreviewService(
            _db,
            registry,
            transports,
            tokens,
            clock,
            new SystemDelay(),
            NullLogger<PreviewService>.Instance
        );

        Services = new AppServices(
            _db,
            bus,
            clock,
            _db,
            Platform,
            ui,
            previews: new LinkPreviews(_db, previews, registry, clock)
        );
        Account = BuildAccount(deviceId, ui, http, registry, connections);
    }

    public AppServices Services { get; }
    public PlatformServices Platform { get; }
    public AccountService Account { get; }

    public ShellViewModel CreateShell()
    {
        // Restores a saved session in the background; the settings page shows the result.
        _ = Account.RestoreAsync(CancellationToken.None);
        return new ShellViewModel(Services, Account);
    }

    AccountService BuildAccount(
        Guid deviceId,
        FileUiState ui,
        HttpClient http,
        ProviderRegistry registry,
        ConnectionService connections
    )
    {
        // Every provider and secret-based method the desktop can connect with. Browser sign-in is listed separately.
        var tokenOptions = registry
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
        var device = new ServerDevice(
            deviceId,
            Environment.MachineName,
            OperatingSystem.IsMacOS() ? "macos"
                : OperatingSystem.IsWindows() ? "windows"
                : "linux"
        );
        // Every provider this build can sign in with. The server says which of them it is set up for.
        // Providers whose browser sign-in can only reach the default host (self-hosted GitLab) are left to the
        // token form, so the user's instance is never bypassed silently.
        var oauthCandidates = registry
            .Providers.Where(p => p.SupportedAuthMethods.Contains(AuthMethod.OAuth2))
            .Where(p => !p.OAuthNeedsInstance)
            .Select(p => new OAuthOption(
                p.ProviderId,
                p.DisplayName,
                Configured: false,
                SetupHint: "",
                ShowSite: p.OAuthAcceptsSite,
                ServerProviderId: GatewayIds.For(p)
            ))
            .OrderBy(o => o.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AccountService(
            new ServerAuthClient(http),
            Platform.Keyring,
            ui,
            device,
            _db,
            connections,
            tokenOptions,
            TimeProvider.System,
            oauthCandidates,
            OpenInBrowser,
            ct => connections.AvailableOAuthAsync(ct)
        );
    }

    // The provider's authorize page. The system browser handles it, so the user's own sign-ins apply.
    static Task OpenInBrowser(Uri url)
    {
        // The URL came from the server. Only an https page is handed to the shell.
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new AuthRequiredException(
                "The server sent a sign-in address that isn't secure. Sign-in was not started."
            );
        try
        {
            using var _ = Process.Start(
                new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }
            );
        }
        catch (Exception e)
            when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new AuthRequiredException(
                "Couldn't open your browser. Open the sign-in page manually and try again."
            );
        }
        return Task.CompletedTask;
    }

    public static string DefaultDataDirectory()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support"
            )
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Noto");
    }

    static Guid DeviceId(string dir)
    {
        var path = Path.Combine(dir, "device.id");
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing))
            return existing;
        var id = Guid.CreateVersion7();
        File.WriteAllText(path, id.ToString());
        return id;
    }

    public void Dispose()
    {
        Platform.Hotkey.Dispose();
        _db.Dispose();
    }
}
