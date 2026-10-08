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
using Noto.Providers.Providers;
using Noto.Providers.Transport;
using Noto.Sync;

namespace Noto.Desktop;

// Wires storage, platform services and the app services. A local install has no account, only a random device id.
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
        Services = new AppServices(_db, bus, clock, _db, Platform, ui);
        Account = BuildAccount(deviceId, ui);
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

    AccountService BuildAccount(Guid deviceId, FileUiState ui)
    {
        var http = new HttpClient();
        var keyring = new KeyringAdapter(Platform.Keyring);
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
        var connections = new ConnectionService(
            _db,
            new CredentialStore(keyring),
            registry,
            new DirectTransportFactory(http, NullLogger<DirectTransport>.Instance),
            new SystemClock(),
            http,
            new Dictionary<string, OAuthClient>()
        );
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
