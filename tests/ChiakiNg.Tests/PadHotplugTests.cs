using System.IO;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP838: the pad that arrives after the picture, which nothing used to open.
///
/// PP835 took the open off the drawing thread so the picture no longer waits for SDL, and a person
/// then plugged one in mid-session: no freeze, no death, and no pad either. Start enumerates once,
/// so the only controller a run could ever use was the one plugged in before it began.
///
/// THESE CANNOT PLUG A DEVICE IN, and saying so is the point. What is provable without one is that
/// the two device events are told apart from input, that a handle is asked rather than a number
/// compared, and that the run reads the pad it is HOLDING rather than the one Start found.
/// </summary>
public class PadHotplugTests(ITestOutputHelper output)
{
    /// <summary>
    /// The two device events are recognised, and nothing else is.
    ///
    /// The numbers are SDL's own - 0x653 and 0x654 - and every input event has to fall through to
    /// the translation. A predicate that matched a button would swallow the presses this whole path
    /// exists to carry.
    /// </summary>
    [Theory]
    [InlineData(0x653u, true, false)]
    [InlineData(0x654u, false, true)]
    [InlineData(0x651u, false, false)]
    [InlineData(0x652u, false, false)]
    [InlineData(0x650u, false, false)]
    [InlineData(0x655u, false, false)]
    public void ArrivalAndDepartureAreToldApartFromInput(uint type, bool arrival, bool departure)
    {
        var ev = new SdlEvent(type, Timestamp: 0, Which: 0, Index: 0, Value: 0);

        Assert.Equal(arrival, Gamepads.IsControllerArrival(ev));
        Assert.Equal(departure, Gamepads.IsControllerDeparture(ev));
    }

    /// <summary>
    /// A handle that was never opened is not attached, which is the question a departure asks.
    ///
    /// The removal carries the instance id of whatever left and this port holds a handle, so the
    /// handle is what gets asked. Zero answering false is what keeps a departure arriving before
    /// any open from calling SDL with nothing.
    /// </summary>
    [Fact]
    public void NoHandleIsNotAnAttachedOne()
        => Assert.False(Gamepads.ControllerIsAttached(IntPtr.Zero));

    /// <summary>
    /// A feed nobody started holds nothing and has adopted nothing.
    ///
    /// The two readings are separate on purpose: zero pushed with one adopted is a pad connected
    /// and not touched, and zero of both is a pad that never arrived. Before PP838 those were the
    /// same number and a run could not tell them apart.
    /// </summary>
    [Fact]
    public void AFeedNobodyStartedHoldsNothing()
    {
        using var feed = new PadFeed(new ManagedControllerStateSink());

        output.WriteLine($"holding {feed.PadName ?? "nothing"}, adopted {feed.Adopted}");

        Assert.False(feed.HasPad);
        Assert.Null(feed.PadName);
        Assert.Equal(0, feed.Adopted);
        Assert.Equal(0, feed.Pushed);
    }

    /// <summary>
    /// PP838: the arm is in the callback, which is the only place SDL delivers an arrival.
    ///
    /// Read out of the source because plugging a device in is what a person does: the fall-through
    /// this replaces was silent, and a rewrite that dropped the arrival back into the translation
    /// would be silent again. Anchored on the calls rather than on the type, for PP764's reason.
    /// </summary>
    [Fact]
    public void TheEventCallbackAdoptsAnArrivalAndReleasesADeparture()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\PadFeed.cs")
            ?? throw new InvalidOperationException("the pad feed is not in this checkout."));

        Assert.Matches(@"(?m)^\s*if \(Gamepads\.IsControllerArrival\(ev\)\)", text);
        Assert.Matches(@"(?m)^\s*if \(Gamepads\.IsControllerDeparture\(ev\)\)", text);

        // And the run reports what is HELD rather than what Start found, which is the reading a
        // mid-session pad changes.
        string run = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs")
            ?? throw new InvalidOperationException("the run is not in this checkout."));

        Assert.Contains("pad.PadName", run, StringComparison.Ordinal);
        Assert.Contains("pad.Adopted", run, StringComparison.Ordinal);
    }
}
