using Noto.App.Services;
using Noto.App.ViewModels;
using Noto.Core.Commands;
using Noto.Core.Time;
using Noto.Data;
using Noto.Platform;
using Noto.Platform.Abstractions;

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
        var bus = new CommandBus(_db, clock, DeviceId(dir));

        Platform = PlatformFactory.Create();
        Services = new AppServices(_db, bus, clock, _db, Platform, new FileUiState(Path.Combine(dir, "ui-state.json")));
    }

    public AppServices Services { get; }
    public PlatformServices Platform { get; }

    public ShellViewModel CreateShell() => new(Services);

    public static string DefaultDataDirectory()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "Noto");
    }

    static Guid DeviceId(string dir)
    {
        var path = Path.Combine(dir, "device.id");
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing)) return existing;
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
