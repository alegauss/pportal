using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP129: SDL's fingers mapped onto the console's two touch slots.
///
/// Driven against the real ChiakiControllerState across the seam, so the ids are the console's
/// own rather than numbers this test invented - which matters, because the thing being checked is
/// a mapping between two namings and inventing one half would check nothing.
/// </summary>
public class TouchpadTrackerTests
{
    [Fact]
    public void TheConsoleHoldsTwoTouches()
    {
        Assert.Equal(2, TouchpadTracker.MaxTouches);
    }

    [Fact]
    public void AFingerDownTakesASlotAndReachesTheState()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        Assert.True(tracker.Down(state, touchpad: 0, finger: 0, 100, 200));
        Assert.Equal(1, tracker.Count);

        ChiakiControllerTouch touch = state.Touch(0);
        Assert.Equal(100, touch.X);
        Assert.Equal(200, touch.Y);
        Assert.True(touch.Id >= 0);
    }

    /// <summary>
    /// The key is the PAIR. A DualSense has one touchpad, so a port keyed on the finger alone
    /// works on every pad anyone testing it owns and collides on a device with two.
    /// </summary>
    [Fact]
    public void TheSameFingerOnADifferentTouchpadIsADifferentTouch()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        Assert.True(tracker.Down(state, touchpad: 0, finger: 0, 10, 10));
        Assert.True(tracker.Down(state, touchpad: 1, finger: 0, 20, 20));
        Assert.Equal(2, tracker.Count);

        // And lifting one leaves the other, which a collision would not.
        Assert.True(tracker.Up(state, touchpad: 0, finger: 0));
        Assert.Equal(1, tracker.Count);
    }

    /// <summary>
    /// A DOWN past the maximum is refused, and nothing is started - so there is no id taken that
    /// the console has no room for and nobody will release.
    /// </summary>
    [Fact]
    public void AThirdFingerIsRefusedWithoutTakingASlot()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        Assert.True(tracker.Down(state, 0, 0, 1, 1));
        Assert.True(tracker.Down(state, 0, 1, 2, 2));
        Assert.False(tracker.Down(state, 0, 2, 3, 3));

        Assert.Equal(2, tracker.Count);
    }

    /// <summary>
    /// Only an UP frees a slot. This is the one whose absence has no symptom until the third
    /// touch of a session, after which the touchpad simply stops working.
    /// </summary>
    [Fact]
    public void LiftingAFingerFreesItsSlotForTheNext()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        tracker.Down(state, 0, 0, 1, 1);
        tracker.Down(state, 0, 1, 2, 2);
        Assert.False(tracker.Down(state, 0, 2, 3, 3));

        Assert.True(tracker.Up(state, 0, 0));
        Assert.True(tracker.Down(state, 0, 2, 3, 3));
        Assert.Equal(2, tracker.Count);
    }

    /// <summary>
    /// A MOTION for a finger that never went down is dropped, not started. Starting it is how a
    /// finger already on the pad during a reconnect consumes a slot nothing ever releases.
    /// </summary>
    [Fact]
    public void AMotionForAnUnknownFingerIsDropped()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        Assert.False(tracker.Motion(state, 0, 0, 500, 500));
        Assert.Equal(0, tracker.Count);
    }

    /// <summary>And a stray UP releases nobody else's touch.</summary>
    [Fact]
    public void AnUpForAnUnknownFingerReleasesNothing()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        tracker.Down(state, 0, 0, 1, 1);
        Assert.False(tracker.Up(state, 0, 9));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void AMotionMovesTheTouchItBelongsTo()
    {
        using var state = new ChiakiControllerState();
        var tracker = new TouchpadTracker();

        tracker.Down(state, 0, 0, 10, 10);
        Assert.True(tracker.Motion(state, 0, 0, 900, 400));

        ChiakiControllerTouch touch = state.Touch(0);
        Assert.Equal(900, touch.X);
        Assert.Equal(400, touch.Y);
    }

    /// <summary>
    /// SDL reports a position as a fraction of the pad and the console wants its own units. The
    /// maxima are per-console, which is what PP93 cost something over.
    /// </summary>
    [Theory]
    [InlineData(0f, 1920, 0)]
    [InlineData(0.5f, 1920, 960)]
    [InlineData(1f, 1920, 1920)]
    [InlineData(0.5f, 1080, 540)]
    public void ANormalisedPositionScalesToTheConsolesUnits(float normalised, int max, int expected)
        => Assert.Equal(expected, TouchpadTracker.ToConsoleUnits(normalised, max));

    /// <summary>The three rules are still the Qt client's, and not only the port's.</summary>
    [Fact]
    public void TheRulesAreStillInTheQtClient()
    {
        string? file = TouchpadSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.True(TouchpadSource.KeyedByTouchpadAndFinger(text), "keyed by the pair");
        Assert.True(TouchpadSource.RefusesBeforeStarting(text), "refused before starting");
        Assert.True(TouchpadSource.UpFreesTheSlot(text), "up frees the slot");
    }
}
