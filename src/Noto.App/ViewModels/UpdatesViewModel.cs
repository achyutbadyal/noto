using System.Diagnostics;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Services;

namespace Noto.App.ViewModels;

// "About & updates": the running version, a manual check, and one-click install where the platform allows it.
public sealed partial class UpdatesViewModel : ObservableObject
{
    public const string AutoCheckKey = "updates-auto";

    readonly UpdateService _service;
    readonly IUiState _state;
    readonly Action<Uri> _open;
    UpdateRelease? _release;

    public UpdatesViewModel(UpdateService service, IUiState state, Action<Uri>? open = null)
    {
        _service = service;
        _state = state;
        _open = open ?? OpenInBrowser;
        _autoCheck = state.Get(AutoCheckKey) != "off";
    }

    public string VersionText => $"Noto {_service.Current}";

    [ObservableProperty]
    string? _status;

    [ObservableProperty]
    bool _isBusy;

    // 0..1 while a download runs.
    [ObservableProperty]
    double _progress;

    [ObservableProperty]
    bool _autoCheck;

    partial void OnAutoCheckChanged(bool value) => _state.Set(AutoCheckKey, value ? null : "off");

    public bool HasUpdate => _release is not null;
    public bool CanInstall => _release is { AssetUrl: not null } && _service.CanInstall;
    public bool ShowNotes => _release is { Notes.Length: > 0 };
    public string Notes => _release?.Notes ?? "";

    // Offered when there is an update the app can't install itself (no matching build, or not a bundle).
    public bool CanOpenPage => HasUpdate && !CanInstall;

    // The host quits the app when this fires: the update is staged and waiting for the process to exit.
    public event Action? RestartRequested;

    void SetRelease(UpdateRelease? release)
    {
        _release = release;
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanOpenPage));
        OnPropertyChanged(nameof(ShowNotes));
        OnPropertyChanged(nameof(Notes));
    }

    // Returns the newer release, or null. Failures (offline, rate-limited) are reported in Status unless
    // the check was silent.
    public async Task<UpdateRelease?> CheckAsync(bool silent = false)
    {
        if (IsBusy)
            return _release;
        IsBusy = true;
        if (!silent)
            Status = "Checking…";
        try
        {
            var release = await _service.CheckAsync();
            SetRelease(release);
            if (!silent || release is not null)
                Status = release is null
                    ? "You're on the latest version."
                    : $"Version {release.Version} is available.";
            return release;
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or TaskCanceledException
                        or System.Text.Json.JsonException
            )
        {
            if (!silent)
                Status = "Couldn't check for updates: " + Describe(e);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    static string Describe(Exception e) =>
        e is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
            ? "no releases published yet."
        : e
            is HttpRequestException
            {
                StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            }
            ? "GitHub is rate-limiting this network, try again later."
        : "check your connection.";

    [RelayCommand]
    Task Check() => CheckAsync();

    [RelayCommand]
    async Task Install()
    {
        if (_release is null || IsBusy)
            return;
        IsBusy = true;
        Progress = 0;
        Status = $"Downloading {_release.Version}…";
        try
        {
            await _service.InstallAsync(_release, new Progress<double>(p => Progress = p));
            Status = "Restarting to finish the update…";
            RestartRequested?.Invoke();
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or InvalidOperationException
                        or TaskCanceledException
            )
        {
            Status = "Update failed: " + e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void OpenReleasePage()
    {
        if (_release is not null)
            _open(_release.PageUrl);
    }

    static void OpenInBrowser(Uri url) =>
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
