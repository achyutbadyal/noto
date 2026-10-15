using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noto.App.Views;

// The section disclosure arrow. Its angle is plain state, so a rebuilt list shows every chevron at
// rest. It only turns when its own section was just toggled: the list is rebuilt on every toggle, so a
// style-driven animation would replay on every expanded header at once.
public sealed class DisclosureChevron : PathIcon
{
    const double ClosedAngle = -90;

    public static readonly StyledProperty<bool> IsCollapsedProperty = AvaloniaProperty.Register<
        DisclosureChevron,
        bool
    >(nameof(IsCollapsed));

    public static readonly StyledProperty<bool> TurnedProperty = AvaloniaProperty.Register<
        DisclosureChevron,
        bool
    >(nameof(Turned));

    readonly RotateTransform _rotate = new();

    public DisclosureChevron() => RenderTransform = _rotate;

    // Styled as a plain PathIcon.
    protected override Type StyleKeyOverride => typeof(PathIcon);

    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    // True when this section was toggled a moment ago, so the turn plays once as the header appears.
    public bool Turned
    {
        get => GetValue(TurnedProperty);
        set => SetValue(TurnedProperty, value);
    }

    double Rest => IsCollapsed ? ClosedAngle : 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCollapsedProperty)
            _rotate.Angle = Rest;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Turned && TopLevel.GetTopLevel(this) is Window w && w.Classes.Contains("motion"))
            _ = Turn();
    }

    async Task Turn()
    {
        var turn = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new ExponentialEaseOut(),
            FillMode = FillMode.None,
        };
        turn.Children.Add(Frame(0, IsCollapsed ? 0 : ClosedAngle));
        turn.Children.Add(Frame(1, Rest));
        await turn.RunAsync(this);
    }

    static KeyFrame Frame(double cue, double angle) =>
        new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(RotateTransform.AngleProperty, angle) },
        };
}
