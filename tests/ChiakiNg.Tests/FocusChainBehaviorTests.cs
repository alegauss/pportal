using System.Windows.Controls;
using System.Windows.Input;
using ChiakiNg.Session;
using Winwright.InApp;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP12: the focus chain bound to real WPF controls.
///
/// Driven against actual Button, CheckBox, ComboBox, RadioButton, Slider and TextBox instances,
/// on an STA thread, without a window. That is the shape PP37 argues for: what is worth asserting
/// about a screen does not need a visual tree, and a port that put this in an event handler over
/// a live window would have made it untestable by construction.
///
/// The controls are created because the attached properties are read off THEM - a decision that
/// works on a DependencyObject and one that works on a Button are not obviously the same until a
/// Button has been asked.
/// </summary>
public class FocusChainBehaviorTests
{
    /// <summary>
    /// Runs on an STA thread, bounded. WPF controls cannot be constructed on an MTA thread, and
    /// a suite that hangs on a UI primitive reports nothing at all.
    /// </summary>

    public static TheoryData<string> ControlKinds() =>
        ["Button", "CheckBox", "ComboBox", "RadioButton", "Slider", "TextBox"];

    private static Control Make(string kind) => kind switch
    {
        "Button" => new Button(),
        "CheckBox" => new CheckBox(),
        "ComboBox" => new ComboBox(),
        "RadioButton" => new RadioButton(),
        "Slider" => new Slider(),
        "TextBox" => new TextBox(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Every control navigates on Up and Down, and consumes the key while doing it. Asserted over
    /// all six because the vocabulary is what makes the screens look like one application - a
    /// Slider that navigated differently would be a screen where the stick behaves oddly in one
    /// row.
    /// </summary>
    [Theory]
    [MemberData(nameof(ControlKinds))]
    public void EveryControlNavigatesOnUpAndDown(string kind) => Apartment.Run(() =>
    {
        Control control = Make(kind);

        Assert.Equal((FocusNavigationDirection.Previous, true), FocusChainBehavior.Decide(control, Key.Up));
        Assert.Equal((FocusNavigationDirection.Next, true), FocusChainBehavior.Decide(control, Key.Down));
    });

    /// <summary>
    /// And not one of them claims Left or Right. A Slider changes its value with them and a
    /// ComboBox its selection, so taking them for navigation would look right on every screen and
    /// break both controls - which is the finding PP12 was corrected for.
    /// </summary>
    [Theory]
    [MemberData(nameof(ControlKinds))]
    public void NoControlClaimsLeftOrRight(string kind) => Apartment.Run(() =>
    {
        Control control = Make(kind);

        Assert.Equal((null, false), FocusChainBehavior.Decide(control, Key.Left));
        Assert.Equal((null, false), FocusChainBehavior.Decide(control, Key.Right));
    });

    /// <summary>
    /// At a boundary focus stays and the key is NOT consumed, so a list hands navigation back to
    /// whatever contains it. Consume it and the list traps focus.
    /// </summary>
    [Fact]
    public void ABoundaryStopsWithoutConsumingTheKey() => Apartment.Run(() =>
    {
        var control = new Button();
        FocusChainBehavior.SetFirstInChain(control, true);

        Assert.Equal((null, false), FocusChainBehavior.Decide(control, Key.Up));
        // The other direction is unaffected, which is what makes it a boundary rather than a wall.
        Assert.Equal((FocusNavigationDirection.Next, true), FocusChainBehavior.Decide(control, Key.Down));

        FocusChainBehavior.SetLastInChain(control, true);
        Assert.Equal((null, false), FocusChainBehavior.Decide(control, Key.Down));
    });

    /// <summary>sendOutput moves focus and lets the key through, which is the whole of that flag.</summary>
    [Fact]
    public void SendOutputNavigatesWithoutConsuming() => Apartment.Run(() =>
    {
        var control = new Button();
        FocusChainBehavior.SetSendOutput(control, true);

        Assert.Equal((FocusNavigationDirection.Next, false), FocusChainBehavior.Decide(control, Key.Down));
    });

    /// <summary>
    /// Return is consumed and moves nothing: confirming is the control's own business - a Button
    /// clicks, a CheckBox toggles - and the behaviour's job is only to stop it navigating.
    /// </summary>
    [Theory]
    [MemberData(nameof(ControlKinds))]
    public void ReturnConfirmsWithoutMovingFocus(string kind) => Apartment.Run(() =>
    {
        Control control = Make(kind);
        Assert.Equal((null, true), FocusChainBehavior.Decide(control, Key.Return));
    });

    /// <summary>The defaults are the Qt client's: nothing is a boundary until a screen says so.</summary>
    [Fact]
    public void AControlIsNotABoundaryUntilItIsToldToBe() => Apartment.Run(() =>
    {
        var control = new Button();
        FocusStop stop = FocusChainBehavior.StopFor(control);

        Assert.False(stop.FirstInChain);
        Assert.False(stop.LastInChain);
        Assert.False(stop.SendOutput);
    });
}
