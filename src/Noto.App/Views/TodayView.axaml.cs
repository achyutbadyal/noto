using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noto.App.Logic;
using Noto.App.ViewModels;
using Noto.Core.Text;

namespace Noto.App.Views;

public partial class TodayView : UserControl
{
    TodayViewModel? _vm;

    public TodayView()
    {
        InitializeComponent();

        // Tunnel so a press anywhere is seen before a row button swallows it: clicking away from the
        // capture bar has to leave it, and only Escape used to work.
        AddHandler(PointerPressedEvent, OnSurfacePointerPressed, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => Attach(DataContext as TodayViewModel);
    }

    void Attach(TodayViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.NewItemRequested -= FocusAddBox;
            _vm.Add.Dismissed -= OnAddDismissed;
            _vm.Add.PropertyChanged -= OnAddChanged;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.NewItemRequested += FocusAddBox;
            _vm.Add.Dismissed += OnAddDismissed;
            _vm.Add.PropertyChanged += OnAddChanged;
        }
        RefreshHighlight();
    }

    void OnAddChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddItemViewModel.Text))
            RefreshHighlight();
    }

    // A TextBox cannot colour runs, so the field's text is transparent and a TextBlock behind it draws
    // the same characters with recognised tokens coloured (docs/07 §7.1).
    void RefreshHighlight()
    {
        AddHighlight.Inlines?.Clear();
        if (_vm is null)
            return;
        var text = _vm.Add.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var parsed = TokenParser.Parse(text, _vm.Snapshot?.Today ?? default);
        var cursor = 0;
        foreach (var token in parsed.Tokens.OrderBy(t => t.Start))
        {
            if (token.Start < cursor || token.Start + token.Text.Length > text.Length)
                continue;
            if (token.Start > cursor)
                AddRun(text[cursor..token.Start], null);
            AddRun(token.Text, BrushFor(token.Kind));
            cursor = token.Start + token.Text.Length;
        }
        if (cursor < text.Length)
            AddRun(text[cursor..], null);
    }

    void AddRun(string text, IBrush? brush)
    {
        if (text.Length == 0)
            return;
        var run = new Avalonia.Controls.Documents.Run(text);
        if (brush is not null)
            run.Foreground = brush;
        AddHighlight.Inlines!.Add(run);
    }

    // Four distinct colours, so a token's kind is readable at a glance; tags and workspaces stay muted.
    // The variant has to be passed explicitly — a theme-dictionary resource is not found without it.
    static IBrush? BrushFor(TokenKind kind)
    {
        var key = kind switch
        {
            TokenKind.Date => "LinkBrush",
            TokenKind.Size => "DoneBrush",
            TokenKind.Priority => "PressureWarmBrush",
            TokenKind.Waiting => "PressureHotBrush",
            _ => "Text2Brush",
        };
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            ? value as IBrush
            : null;
    }

    void FocusAddBox() => Dispatcher.UIThread.Post(() => AddBox.Focus());

    // Any dismissal (Escape, the scrim, a click outside) drops focus so the caret stops blinking.
    void OnAddDismissed() =>
        Dispatcher.UIThread.Post(() => TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus());

    // A press outside the capture bar leaves the field, keeping whatever was typed.
    void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || e.Source is not Visual source)
            return;
        if (IsWithin(source, CaptureBar) || IsWithin(source, DetailedPanel))
            return;
        if (!_vm.Add.IsDetailedOpen && _vm.Add.Text.Length == 0 && !AddBox.IsFocused)
            return;
        _vm.Add.Dismiss(keepTitle: true);
    }

    // Dropdown lists are hosted in an overlay, outside the visual tree of the control that opened them,
    // but they stay in its logical tree. So a press on one of their items still counts as inside.
    static bool IsWithin(Visual node, Visual ancestor) =>
        ReferenceEquals(node, ancestor)
        || node.GetVisualAncestors().Contains(ancestor)
        || node.GetLogicalAncestors().Contains(ancestor);

    // Enter adds; Backspace at the end of a recognized token removes the whole token; Escape leaves the box.
    async void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _vm.Add.SubmitAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _vm.Add.Dismiss();
                break;
            case Key.Back
                when AddBox.SelectionStart == AddBox.SelectionEnd
                    && AddBox.CaretIndex == (AddBox.Text?.Length ?? 0):
                var before = _vm.Add.Text;
                var after = TokenParser.RemoveTrailingToken(
                    before,
                    _vm.Snapshot?.Today ?? default,
                    recognizeTags: false
                );
                if (before.Length - after.Length > 1)
                {
                    e.Handled = true;
                    _vm.Add.Text = after;
                    AddBox.CaretIndex = after.Length;
                }
                break;
            case Key.V
                when e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                    || e.KeyModifiers.HasFlag(KeyModifiers.Control):
                if (await TryPasteUrlAsync())
                    e.Handled = true;
                break;
        }
    }

    // Paste-to-task: a lone URL becomes the task, and because a URL in the title is indexed as a link
    // automatically, the item comes out attached to the page it came from (docs/07 §6.4).
    async Task<bool> TryPasteUrlAsync()
    {
        if (_vm is null || _vm.Add.Text.Length > 0)
            return false;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return false;

        var transfer = await clipboard.TryGetDataAsync();
        if (transfer is null)
            return false;
        using (transfer)
        {
            var text = (await transfer.TryGetTextAsync())?.Trim();
            if (
                string.IsNullOrEmpty(text)
                || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            )
                return false;

            _vm.Add.Text = text;
            AddBox.CaretIndex = text.Length;
            RefreshHighlight();
            return true;
        }
    }

    // Enter submits the panel; Escape closes it. The notes box keeps Enter for new lines.
    async void OnDetailKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await _vm.Add.SubmitDetailedAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                _vm.Add.Dismiss(keepTitle: true);
                break;
        }
    }

    void OnDetailedScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        _vm?.Add.Dismiss(keepTitle: true);
    }
}
