using System.Windows.Controls;
using System.Windows.Media;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP12: how a focused control shows it - and the gap reading the Qt client turned up.
/// </summary>
public class FocusVisualTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>
    /// The finding. PP12 asks for a focus visual legible from three metres; the Qt client has one
    /// on exactly one of six controls, and the other five inherit the thin default ring that
    /// requirement exists to replace.
    ///
    /// Asserted as ONE rather than as "Button has it", so that a Qt client which gave another
    /// control the treatment is a change someone looks at rather than a divergence that quietly
    /// appears in a port nobody re-read.
    /// </summary>
    [Fact]
    public void OnlyOneQtControlDeclaresAFocusVisual()
    {
        if (FocusChainSource.Locate("Button") is null)
            return;

        Assert.Equal<string[]>(["Button"], [.. FocusVisualSource.WithFocusVisual()]);
    }

    /// <summary>
    /// And the rule itself: the accent as BACKGROUND while focused. A filled background is the
    /// part that reads across a room, so porting it as a border would meet the letter and lose
    /// the reason.
    /// </summary>
    [Fact]
    public void TheRuleIsAnAccentBackgroundWhileFocused() => OnSta(() =>
    {
        var accent = new SolidColorBrush(Colors.DodgerBlue);
        var button = new Button();
        FocusVisual.SetUseAccentBackground(button, true);

        Assert.Same(accent, FocusVisual.BackgroundFor(button, focused: true, accent));

        // Null, not Transparent: the Qt rule is `undefined`, meaning "whatever the style would
        // have done". Painting transparent would erase a themed background rather than defer.
        Assert.Null(FocusVisual.BackgroundFor(button, focused: false, accent));
    });

    /// <summary>
    /// A control that has not opted in gets nothing even while focused - which is the port
    /// matching the client rather than improving on it, because "no redesign while porting" is a
    /// binding non-goal and giving five controls a filled background is a visible redesign.
    /// </summary>
    [Fact]
    public void AControlThatHasNotOptedInIsLeftToTheTheme() => OnSta(() =>
    {
        var accent = new SolidColorBrush(Colors.DodgerBlue);

        foreach (Control control in new Control[]
                 { new CheckBox(), new ComboBox(), new RadioButton(), new Slider(), new TextBox() })
        {
            Assert.Null(FocusVisual.BackgroundFor(control, focused: true, accent));
        }
    });

    /// <summary>
    /// PP12: the accent brush the focus visual needs actually resolves under the Fluent theme.
    ///
    /// FocusVisual takes the accent as an argument rather than reaching for it, which keeps it
    /// testable - but a rule about the accent is worth nothing if the application cannot name one.
    /// ThemeMode="System" is set in App.xaml (PP1) and the accent is the user's Windows setting,
    /// so this asks the system for it the way a control style would.
    /// </summary>
    [Fact]
    public void TheSystemAccentBrushResolves() => OnSta(() =>
    {
        Brush accent = System.Windows.SystemColors.AccentColorBrush;

        Assert.NotNull(accent);
        Assert.True(accent.IsFrozen || accent.CanFreeze, "the accent brush is not usable as a resource");

        // And it is a real colour rather than the default black a missing resource resolves to,
        // which is what a theme that failed to load would leave behind.
        var solid = Assert.IsAssignableFrom<SolidColorBrush>(accent);
        Assert.NotEqual(Colors.Transparent, solid.Color);
    });
}