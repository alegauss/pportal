using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace ChiakiNg.Session;

/// <summary>
/// PP12: how a focused control shows it, and the gap that reading the Qt client turned up.
///
/// The task asks for "a focus visual that is legible from three metres", which is the right thing
/// for a couch application to want. The Qt client has one on exactly ONE of its six controls:
///
///   Material.background: visualFocus ? Material.accent : undefined
///
/// That is Button.qml. CheckBox, ComboBox, RadioButton, Slider and TextField declare no focus
/// visual at all and take whatever the Material style does by default - which is the thin ring
/// the three-metre requirement exists to replace.
///
/// So the port faces a choice it did not expect: match the client, or fix it. It matches. "No
/// redesign while porting" is a binding non-goal, and giving five controls a filled accent
/// background they have never had is a redesign - a visible one, on every screen. The gap is
/// recorded rather than closed, because whether to close it is not this port's call.
///
/// What is ported is the rule Button does have, spelled out rather than inherited: the accent
/// brush becomes the background while focused, and NOT a border - a filled background is the part
/// that reads across a room, and swapping it for an outline would meet the letter of the port and
/// lose the reason.
/// </summary>
public static class FocusVisual
{
    /// <summary>Whether this control takes the accent-background treatment. Only Button, per the Qt client.</summary>
    public static readonly DependencyProperty UseAccentBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "UseAccentBackground", typeof(bool), typeof(FocusVisual), new PropertyMetadata(false));

    public static bool GetUseAccentBackground(DependencyObject o)
        => (bool)o.GetValue(UseAccentBackgroundProperty);

    public static void SetUseAccentBackground(DependencyObject o, bool value)
        => o.SetValue(UseAccentBackgroundProperty, value);

    /// <summary>
    /// The background a control should paint, or null to leave it to the theme.
    ///
    /// Null and not Transparent: the Qt rule is `undefined`, which means "whatever the style would
    /// have done", and painting transparent instead would erase a themed background rather than
    /// defer to it.
    /// </summary>
    public static Brush? BackgroundFor(DependencyObject control, bool focused, Brush accent)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(accent);

        return focused && GetUseAccentBackground(control) ? accent : null;
    }
}

/// <summary>
/// PP12: what the six QML controls declare about focus, read out of them.
/// </summary>
public static partial class FocusVisualSource
{
    /// <summary>Whether a control paints the accent as its background while focused.</summary>
    public static bool UsesAccentBackground(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return AccentRegex().IsMatch(qml);
    }

    /// <summary>
    /// The controls that declare any focus visual at all. One, at the time of writing, and the
    /// count is asserted so that a Qt client which gave the other five one is a change someone
    /// sees rather than a divergence that quietly appears.
    /// </summary>
    public static IReadOnlyList<string> WithFocusVisual()
    {
        var found = new List<string>();
        foreach (string control in FocusChainSource.Controls)
        {
            string? file = FocusChainSource.Locate(control);
            if (file is null)
                return [];
            if (UsesAccentBackground(File.ReadAllText(file)))
                found.Add(control);
        }

        return found;
    }

    [GeneratedRegex(@"Material\.background:\s*visualFocus\s*\?\s*Material\.accent\s*:\s*undefined")]
    private static partial Regex AccentRegex();
}
