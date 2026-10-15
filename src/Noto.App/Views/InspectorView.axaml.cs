using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noto.App.ViewModels;

namespace Noto.App.Views;

public partial class InspectorView : UserControl
{
    InspectorViewModel? _vm;
    bool _editingNotes;

    public InspectorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as InspectorViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmChanged;
            _editingNotes = false;
            UpdateNotesMode();
        };
        NotesBox.GotFocus += (_, _) =>
        {
            _editingNotes = true;
            UpdateNotesMode();
        };
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Selecting another task starts it in reading mode; typing does not flip the editor away.
        if (e.PropertyName == nameof(InspectorViewModel.Item))
            _editingNotes = false;
        if (e.PropertyName is nameof(InspectorViewModel.Item) or nameof(InspectorViewModel.Notes))
            UpdateNotesMode();
    }

    // Plain notes stay in the editor, exactly as before. Notes with a web link show as text with working
    // links until you press in them.
    void UpdateNotesMode()
    {
        var reading = !_editingNotes && Noto.Core.Links.LinkUrl.Spans(_vm?.Notes).Count > 0;
        NotesRead.IsVisible = reading;
        NotesBox.IsVisible = !reading;
    }

    // Tabbing onto the notes goes straight to the editor.
    void OnNotesReadGotFocus(object? sender, GotFocusEventArgs e) => BeginEditNotes();

    void OnNotesReadPressed(object? sender, PointerPressedEventArgs e) =>
        // A press on a link is handled (and opened) by the link itself before it gets here.
        BeginEditNotes();

    void BeginEditNotes()
    {
        _editingNotes = true;
        UpdateNotesMode();
        Dispatcher.UIThread.Post(() =>
        {
            NotesBox.Focus();
            NotesBox.CaretIndex = NotesBox.Text?.Length ?? 0;
        });
    }

    async void OnLinkTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string url } control)
        {
            e.Handled = true;
            await LinkLauncher.OpenAsync(control, url);
        }
    }

    async void OnNotesLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            await vm.SaveNotesAsync();
        _editingNotes = false;
        UpdateNotesMode();
    }

    // The title and the waiting-on name commit on Enter or when focus leaves, never per keystroke.
    async void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InspectorViewModel vm)
        {
            e.Handled = true;
            await vm.CommitTitleAsync();
        }
    }

    async void OnTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            await vm.CommitTitleAsync();
    }

    async void OnWaitingKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is InspectorViewModel vm)
        {
            e.Handled = true;
            await vm.WaitingChangedAsync();
        }
    }

    async void OnWaitingLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            await vm.WaitingChangedAsync();
    }

    // Opens the link in the default browser. Only web links are launched; the scanner never produces others.
    async void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } button)
            await LinkLauncher.OpenAsync(button, url);
    }
}
