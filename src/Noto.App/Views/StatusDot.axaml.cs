using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noto.App.Views;

public partial class StatusDot : UserControl
{
    public static readonly StyledProperty<string?> GlyphProperty = AvaloniaProperty.Register<
        StatusDot,
        string?
    >(nameof(Glyph));

    public static readonly StyledProperty<bool> ChangedProperty = AvaloniaProperty.Register<
        StatusDot,
        bool
    >(nameof(Changed));

    static readonly string[] States = ["planned", "now", "waiting", "done", "dropped"];

    public StatusDot()
    {
        InitializeComponent();
        // All three are scaled about the centre, so a state change grows from the middle of the dot.
        Core.RenderTransform = new ScaleTransform(1, 1);
        Ripple.RenderTransform = new ScaleTransform(1, 1);
        Ring.RenderTransform = new ScaleTransform(1, 1);
    }

    // planned / now / waiting / done / dropped.
    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    // True when the task changed state moments ago, so this dot plays the transition once on appearing.
    public bool Changed
    {
        get => GetValue(ChangedProperty);
        set => SetValue(ChangedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != GlyphProperty)
            return;
        foreach (var state in States)
            Classes.Set(state, state == Glyph);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (
            Changed
            && TopLevel.GetTopLevel(this) is Window { } window
            && window.Classes.Contains("motion")
        )
            _ = Play();
    }

    // Completing: the green disc grows out of the ring while a ripple leaves it. Any other change: the
    // new dot settles in from slightly small. Exponential ease-out throughout, and no overshoot.
    async Task Play()
    {
        if (Glyph == "done")
        {
            await Task.WhenAll(
                Frames(0.35, 1, 280).RunAsync(Core),
                Frames(1, 1.9, 460, fadeOut: true).RunAsync(Ripple)
            );
            return;
        }
        await Task.WhenAll(Frames(0.6, 1, 220).RunAsync(Core), Frames(0.6, 1, 220).RunAsync(Ring));
    }

    static Animation Frames(double from, double to, int ms, bool fadeOut = false)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = new ExponentialEaseOut(),
        };
        animation.Children.Add(Key(0, from, fadeOut ? 0.5 : null));
        animation.Children.Add(Key(1, to, fadeOut ? 0 : null));
        return animation;
    }

    static KeyFrame Key(double cue, double scale, double? opacity)
    {
        var frame = new KeyFrame
        {
            Cue = new Cue(cue),
            Setters =
            {
                new Setter(ScaleTransform.ScaleXProperty, scale),
                new Setter(ScaleTransform.ScaleYProperty, scale),
            },
        };
        if (opacity is { } o)
            frame.Setters.Add(new Setter(OpacityProperty, o));
        return frame;
    }
}
