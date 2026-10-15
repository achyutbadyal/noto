using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Noto.App.ViewModels;
using Noto.Core.Text;

namespace Noto.App.Views;

// The "new task" bar: a title field that colours the tokens it recognises, a duration, Add and More options.
// Today and the day planner both use it; it is driven by that page's AddItemViewModel.
public partial class CaptureComposer : UserControl
{
    public static readonly StyledProperty<string?> WatermarkProperty = AvaloniaProperty.Register<
        CaptureComposer,
        string?
    >(nameof(Watermark), "New task…");

    AddItemViewModel? _vm;

    public CaptureComposer()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as AddItemViewModel);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    AddItemViewModel? Vm => _vm;

    // The border that holds the field, so a page can tell a press inside the bar from one outside it.
    public Control Bar => BarBorder;

    public bool IsInputFocused => AddBox.IsFocused;

    public void FocusInput() => Dispatcher.UIThread.Post(() => AddBox.Focus());

    void Attach(AddItemViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnAddChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnAddChanged;
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
        if (Vm is null)
            return;
        var text = Vm.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var parsed = TokenParser.Parse(text, Vm.Today);
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

    // Enter adds; Backspace at the end of a recognized token removes the whole token; Escape leaves the box.
    async void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await Vm.SubmitAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                Vm.Dismiss();
                break;
            case Key.Back
                when AddBox.SelectionStart == AddBox.SelectionEnd
                    && AddBox.CaretIndex == (AddBox.Text?.Length ?? 0):
                var before = Vm.Text;
                var after = TokenParser.RemoveTrailingToken(before, Vm.Today, recognizeTags: false);
                if (before.Length - after.Length > 1)
                {
                    e.Handled = true;
                    Vm.Text = after;
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
        if (Vm is null || Vm.Text.Length > 0)
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

            Vm.Text = text;
            AddBox.CaretIndex = text.Length;
            RefreshHighlight();
            return true;
        }
    }
}
