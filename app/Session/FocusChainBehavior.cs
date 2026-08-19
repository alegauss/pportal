using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChiakiNg.Session;

/// <summary>
/// PP12: the focus chain attached to real controls.
///
/// <see cref="FocusChain"/> settled what the Qt client does - Up and Down walk a tab chain, both
/// ends guarded by a property of the control, and left and right left alone. This is that bound
/// to WPF, and the binding is where a port that agreed in principle diverges in practice.
///
/// Attached properties rather than six subclasses. The Qt side is six QML files each declaring
/// firstInFocusChain and lastInFocusChain and handling the same three keys; six WPF subclasses
/// would be the same duplication in a language that does not need it, and a screen would then
/// have to remember to use ChiakiButton rather than Button. An attached property is opt-in per
/// control and works on anything focusable, including the ones this port has not written yet.
///
/// What it deliberately does NOT do is claim Left or Right. A Slider changes its value with them
/// and a ComboBox its selection; taking them for navigation would look right on every screen and
/// break both controls.
/// </summary>
public static class FocusChainBehavior
{
    /// <summary>Up does not leave this control - the key falls through to whatever contains it.</summary>
    public static readonly DependencyProperty FirstInChainProperty =
        DependencyProperty.RegisterAttached(
            "FirstInChain", typeof(bool), typeof(FocusChainBehavior), new PropertyMetadata(false));

    /// <summary>Down does not leave this control.</summary>
    public static readonly DependencyProperty LastInChainProperty =
        DependencyProperty.RegisterAttached(
            "LastInChain", typeof(bool), typeof(FocusChainBehavior), new PropertyMetadata(false));

    /// <summary>
    /// The key moves focus AND reaches the control. The Qt client sets event.accepted only when
    /// this is false, so it is the whole of the flag.
    /// </summary>
    public static readonly DependencyProperty SendOutputProperty =
        DependencyProperty.RegisterAttached(
            "SendOutput", typeof(bool), typeof(FocusChainBehavior), new PropertyMetadata(false));

    public static bool GetFirstInChain(DependencyObject o) => (bool)o.GetValue(FirstInChainProperty);
    public static void SetFirstInChain(DependencyObject o, bool value) => o.SetValue(FirstInChainProperty, value);

    public static bool GetLastInChain(DependencyObject o) => (bool)o.GetValue(LastInChainProperty);
    public static void SetLastInChain(DependencyObject o, bool value) => o.SetValue(LastInChainProperty, value);

    public static bool GetSendOutput(DependencyObject o) => (bool)o.GetValue(SendOutputProperty);
    public static void SetSendOutput(DependencyObject o, bool value) => o.SetValue(SendOutputProperty, value);

    /// <summary>The stop this control represents, read off its attached properties.</summary>
    public static FocusStop StopFor(DependencyObject control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return new FocusStop(GetFirstInChain(control), GetLastInChain(control), GetSendOutput(control));
    }

    /// <summary>
    /// What a key press should do to a control, without touching WPF's focus at all.
    ///
    /// Separated from the event handler because this is the part worth asserting: moving focus
    /// needs a visual tree and a window, and deciding whether to move it needs neither. PP37's
    /// whole argument is that a port which buries this in an event handler cannot test it.
    /// </summary>
    /// <returns>
    /// The direction to move, or null to leave focus alone; and whether the key is consumed.
    /// </returns>
    public static (FocusNavigationDirection? Move, bool Handled) Decide(DependencyObject control, Key key)
    {
        ArgumentNullException.ThrowIfNull(control);

        FocusStop stop = StopFor(control);

        switch (key)
        {
            case Key.Up when !stop.FirstInChain:
                return (FocusNavigationDirection.Previous, !stop.SendOutput);
            case Key.Up:
                // At the boundary the key is NOT consumed, which is how a list hands navigation
                // back to what contains it - consume it and the list traps focus.
                return (null, false);

            case Key.Down when !stop.LastInChain:
                return (FocusNavigationDirection.Next, !stop.SendOutput);
            case Key.Down:
                return (null, false);

            // Confirm is the control's own business - a Button clicks, a CheckBox toggles - so
            // this reports the key as consumed and leaves the doing to the control.
            case Key.Return:
                return (null, true);

            // And everything else, Left and Right included, is untouched on purpose.
            default:
                return (null, false);
        }
    }

    /// <summary>
    /// Installs the behaviour on a control. The handler is the thin half: it asks
    /// <see cref="Decide"/> and then does the one thing WPF offers for it.
    /// </summary>
    public static void Attach(UIElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.KeyDown += OnKeyDown;
    }

    public static void Detach(UIElement control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.KeyDown -= OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not UIElement control)
            return;

        (FocusNavigationDirection? move, bool handled) = Decide(control, e.Key);

        if (move is FocusNavigationDirection direction)
            control.MoveFocus(new TraversalRequest(direction));

        e.Handled = handled;
    }
}
