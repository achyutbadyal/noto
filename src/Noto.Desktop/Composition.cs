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
        var oauthClients = new Dictionary<string, OAuthClient>();
        var tokens = new TokenManager(_db, credentials, registry, clock, http, oauthClients);
        var connections = new ConnectionService(
            _db,
            credentials,
            registry,
            transports,
            clock,
            http,
            oauthClients
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
        // Every provider and secret-based method the desktop can connect with (OAuth is not wired in yet).
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
        return new AccountService(
            new ServerAuthClient(http),
            Platform.Keyring,
            ui,
            device,
            _db,
            connections,
            tokenOptions,
            TimeProvider.System
        );
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
