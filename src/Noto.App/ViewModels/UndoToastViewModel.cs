using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Services;

namespace Noto.App.ViewModels;

// "Undo instead of confirm": every action surfaces here for a few seconds.
public sealed partial class UndoToastViewModel : ObservableObject
{
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(6);

    readonly UndoService _undo;
    readonly SynchronizationContext? _context = SynchronizationContext.Current;
    CancellationTokenSource? _hide;

    public UndoToastViewModel(UndoService undo)
    {
        _undo = undo;
        undo.Pushed += entry => Show(entry.Label, canUndo: true);
        undo.Undone += entry => Show($"Undid: {entry.Label}", canUndo: false);
    }

    [ObservableProperty] string _message = "";
    [ObservableProperty] bool _isVisible;
    [ObservableProperty] bool _canUndo;

    public event Action<string>? MessageShown;

    public void Show(string message, bool canUndo)
    {
        Message = message;
        CanUndo = canUndo;
        IsVisible = true;
        MessageShown?.Invoke(message);

        _hide?.Cancel();
        var cts = _hide = new CancellationTokenSource();
        _ = Task.Delay(Visible, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            if (_context is null) IsVisible = false;
            else _context.Post(_ => IsVisible = false, null);
        });
    }

    [RelayCommand]
    public async Task UndoAsync()
    {
        IsVisible = false;
        await _undo.UndoLastAsync();
    }

    [RelayCommand]
    public void Dismiss() => IsVisible = false;
}
