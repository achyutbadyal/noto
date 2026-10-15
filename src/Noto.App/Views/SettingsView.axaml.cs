using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    SettingsViewModel? Vm => DataContext as SettingsViewModel;
    UpdatesViewModel? _updates;

    // A staged update is waiting for this process to exit, so "restart" means quit; the helper relaunches.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_updates is not null)
            _updates.RestartRequested -= Quit;
        _updates = Vm?.Updates;
        if (_updates is not null)
            _updates.RestartRequested += Quit;
    }

    static void Quit() =>
        (
            Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime
        )?.Shutdown();

    async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = "Import", AllowMultiple = false }
        );
        if (files.Count == 0)
            return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        await vm.ImportAsync(await reader.ReadToEndAsync());
    }

    void OnExportJsonClick(object? sender, RoutedEventArgs e) =>
        _ = SaveAsync("noto-export.json", "json", () => Vm!.ExportJsonAsync());

    void OnExportCsvClick(object? sender, RoutedEventArgs e) =>
        _ = SaveAsync("noto-items.csv", "csv", () => Vm!.ExportCsvAsync());

    async Task SaveAsync(string name, string extension, Func<Task<string>> produce)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        var file = await storage.SaveFilePickerAsync(
            new FilePickerSaveOptions { SuggestedFileName = name, DefaultExtension = extension }
        );
        if (file is null)
            return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(await produce());
        vm.DataStatus = $"Exported to {file.Name}";
    }
}
