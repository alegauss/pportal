using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP12: how focus moves between controls, and what the six QML controls actually bind.
///
/// The task was filed expecting a directional focus engine. There isn't one - which is the
/// finding, and the reason these tests are about a chain rather than about geometry.
/// </summary>
public class FocusChainTests
{
    private static FocusChain ThreeStops() => new(
    [
        new FocusStop(FirstInChain: true),
        new FocusStop(),
        new FocusStop(LastInChain: true),
    ]);

    [Fact]
    public void DownWalksForwardAndUpWalksBack()
    {
        FocusChain chain = ThreeStops();

        Assert.Equal(new FocusMove(1, true), chain.Down(0));
        Assert.Equal(new FocusMove(2, true), chain.Down(1));
        Assert.Equal(new FocusMove(1, true), chain.Up(2));
        Assert.Equal(new FocusMove(0, true), chain.Up(1));
    }

    /// <summary>
    /// At a boundary focus does not move AND the key is not consumed. The second half is what
    /// lets a list inside a screen hand navigation back out to whatever contains it - consume it
    /// and the outer screen never sees the press, which is a list you cannot leave.
    /// </summary>
    [Fact]
    public void AtABoundaryFocusStaysAndTheKeyIsNotConsumed()
    {
        FocusChain chain = ThreeStops();

        Assert.Equal(new FocusMove(0, false), chain.Up(0));
        Assert.Equal(new FocusMove(2, false), chain.Down(2));
    }

    /// <summary>
    /// The boundary is a property of the control, not of its position: a stop can be marked first
    /// while sitting in the middle, which is how one visual column holds two chains.
    /// </summary>
    [Fact]
    public void ABoundaryInTheMiddleStillStops()
    {
        var chain = new FocusChain(
        [
            new FocusStop(),
            new FocusStop(FirstInChain: true),
            new FocusStop(),
        ]);

        Assert.Equal(new FocusMove(1, false), chain.Up(1));
        Assert.Equal(new FocusMove(2, true), chain.Down(1));
    }

    /// <summary>
    /// sendOutput lets a control move focus and still act on the key. The Qt client sets
    /// event.accepted only when it is false, which is the whole of the flag.
    /// </summary>
    [Fact]
    public void SendOutputMovesFocusWithoutConsumingTheKey()
    {
        var chain = new FocusChain([new FocusStop(SendOutput: true), new FocusStop()]);

        Assert.Equal(new FocusMove(1, false), chain.Down(0));
    }

    /// <summary>
    /// The finding: not one of the six controls handles Left or Right. They are not missing, they
    /// are left alone - a slider changes its value with them and a combo box its selection. A
    /// port that claimed them for navigation would take that away, and the screens would still
    /// look right.
    /// </summary>
    [Fact]
    public void NoControlClaimsLeftOrRight()
    {
        foreach (string control in FocusChainSource.Controls)
        {
            string? file = FocusChainSource.Locate(control);
            if (file is null)
                return;

            IReadOnlySet<string> keys = FocusChainSource.KeysHandled(File.ReadAllText(file));

            Assert.DoesNotContain("Left", keys);
            Assert.DoesNotContain("Right", keys);
        }
    }

    /// <summary>Every control walks the tab chain, in both directions, guarded at both ends.</summary>
    [Fact]
    public void EveryControlWalksTheTabChainGuardedAtBothEnds()
    {
        foreach (string control in FocusChainSource.Controls)
        {
            string? file = FocusChainSource.Locate(control);
            if (file is null)
                return;

            string qml = File.ReadAllText(file);
            IReadOnlySet<string> keys = FocusChainSource.KeysHandled(qml);

            Assert.Contains("Up", keys);
            Assert.Contains("Down", keys);
            Assert.True(FocusChainSource.WalksTheTabChain(qml), control + " walks the chain");
            Assert.True(FocusChainSource.GuardsBothEnds(qml), control + " guards both ends");
        }
    }

    /// <summary>
    /// Confirm and cancel are not uniform, and the exceptions are the interesting part. Five
    /// controls take Return; the Slider does not, having nothing to confirm. One control takes
    /// Escape - the TextField - so cancel is a text-entry affordance rather than a global back.
    /// </summary>
    [Fact]
    public void ConfirmAndCancelAreNotUniform()
    {
        if (FocusChainSource.Locate("Button") is null)
            return;

        var withReturn = new List<string>();
        var withEscape = new List<string>();

        foreach (string control in FocusChainSource.Controls)
        {
            IReadOnlySet<string> keys =
                FocusChainSource.KeysHandled(File.ReadAllText(FocusChainSource.Locate(control)!));

            if (keys.Contains("Return"))
                withReturn.Add(control);
            if (keys.Contains("Escape"))
                withEscape.Add(control);
        }

        Assert.Equal<string[]>(["Button", "CheckBox", "ComboBox", "RadioButton", "TextField"],
            [.. withReturn]);
        Assert.Equal<string[]>(["TextField"], [.. withEscape]);
    }
}
