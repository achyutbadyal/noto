using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Noto.App.Views;

public static class Behaviors
{
    // Focuses the control whenever it becomes visible (prompts, the command bar, inline editors).
    public static readonly AttachedProperty<bool> FocusWhenVisibleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("FocusWhenVisible", typeof(Behaviors));

    static Behaviors()
    {
        FocusWhenVisibleProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is not true) return;
            control.GetObservable(Visual.IsVisibleProperty).Subscribe(new Observer(visible =>
            {
                if (visible) Dispatcher.UIThread.Post(() => { control.Focus(); if (control is TextBox box) box.CaretIndex = box.Text?.Length ?? 0; });
            }));
        });
    }

    public static bool GetFocusWhenVisible(Control c) => c.GetValue(FocusWhenVisibleProperty);
    public static void SetFocusWhenVisible(Control c, bool value) => c.SetValue(FocusWhenVisibleProperty, value);

    sealed class Observer(Action<bool> onNext) : IObserver<bool>
    {
        public void OnNext(bool value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
