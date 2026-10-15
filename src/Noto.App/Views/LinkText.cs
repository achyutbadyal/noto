using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Noto.Core.Links;

namespace Noto.App.Views;

// Text whose web links are links: coloured, underlined, and opened on click. It is a TextBlock, so it takes
// the same classes, trimming and wrapping; it only changes how the runs are built and what a press does.
// A press on a link opens it and stops there, so the row or card around the text is not also selected.
public sealed class LinkText : TextBlock
{
    public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<
        LinkText,
        string?
    >(nameof(Value));

    IReadOnlyList<LinkSpan> _spans = [];

    static LinkText() => ValueProperty.Changed.AddClassHandler<LinkText>((t, _) => t.Rebuild());

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    void Rebuild()
    {
        var text = Value ?? "";
        _spans = LinkUrl.Spans(text);
        Inlines?.Clear();
        if (_spans.Count == 0)
        {
            Text = text;
            return;
        }

        Text = null;
        var link = LinkBrush();
        var at = 0;
        foreach (var span in _spans)
        {
            if (span.Start > at)
                Inlines!.Add(new Run(text[at..span.Start]));
            Inlines!.Add(
                new Run(text.Substring(span.Start, span.Length))
                {
                    Foreground = link,
                    TextDecorations = Avalonia.Media.TextDecorations.Underline,
                }
            );
            at = span.Start + span.Length;
        }
        if (at < text.Length)
            Inlines!.Add(new Run(text[at..]));
    }

    IBrush LinkBrush() =>
        this.TryFindResource("LinkBrush", ActualThemeVariant, out var b) && b is IBrush brush
            ? brush
            : Brushes.RoyalBlue;

    string? LinkAt(Point point)
    {
        if (_spans.Count == 0)
            return null;
        var hit = TextLayout.HitTestPoint(point - new Point(Padding.Left, Padding.Top));
        if (!hit.IsInside)
            return null;
        foreach (var span in _spans)
            if (hit.TextPosition >= span.Start && hit.TextPosition < span.Start + span.Length)
                return span.Url;
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = LinkAt(e.GetPosition(this)) is null ? null : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = null;
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && LinkAt(e.GetPosition(this)) is { } url
        )
        {
            e.Handled = true;
            await LinkLauncher.OpenAsync(this, url);
        }
    }
}
