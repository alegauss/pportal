using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP222: a trigger pull as one line, and why the triggers can be shown when the sticks cannot.
///
/// Measured on a DualSense: one pull of R2 produced 509 axis events, the axis reporting every step
/// of its travel. One line per crossing is the difference between a diagnostic somebody can read
/// and the flood PP220 turned the analog opt-in off to escape.
/// </summary>
public class TriggerPullsTests
{
    private static SdlEvent Axis(byte index, short value)
        => new(Gamepads.EventType.JoyAxisMotion, 0, 0, index, 0, value);

    private const byte L2 = 4;
    private const byte R2 = 5;
    private const byte LeftStickY = 1;

    /// <summary>Only the two trigger axes, which are the ones the mapping string names as such.</summary>
    [Fact]
    public void OnlyTheTriggerAxesCount()
    {
        Assert.True(TriggerPulls.IsTriggerAxis(L2));
        Assert.True(TriggerPulls.IsTriggerAxis(R2));

        Assert.False(TriggerPulls.IsTriggerAxis(0));
        Assert.False(TriggerPulls.IsTriggerAxis(LeftStickY));
        Assert.False(TriggerPulls.IsTriggerAxis(3));
    }

    /// <summary>
    /// A whole pull is ONE line. The 509 events a real pull produced would otherwise be 509 lines,
    /// which is the flood in a different costume.
    /// </summary>
    [Fact]
    public void AWholePullIsOneLine()
    {
        var pulls = new TriggerPulls();

        // Released, at the joystick layer's minimum rather than at zero.
        Assert.Null(pulls.Pull(Axis(R2, short.MinValue)));

        // The travel: one crossing, then more of the same press.
        Assert.Null(pulls.Pull(Axis(R2, -20000)));
        Assert.Equal("a5", pulls.Pull(Axis(R2, 4000)));
        Assert.Null(pulls.Pull(Axis(R2, 19146)));
        Assert.Null(pulls.Pull(Axis(R2, short.MaxValue)));
    }

    /// <summary>Releasing says nothing, and the next pull says it again.</summary>
    [Fact]
    public void ReleasingIsSilentAndTheNextPullIsNot()
    {
        var pulls = new TriggerPulls();

        Assert.Equal("a5", pulls.Pull(Axis(R2, short.MaxValue)));
        Assert.Null(pulls.Pull(Axis(R2, short.MinValue)));
        Assert.Equal("a5", pulls.Pull(Axis(R2, 1)));
    }

    /// <summary>The two triggers are counted apart, so one being held does not hide the other.</summary>
    [Fact]
    public void TheTwoTriggersAreCountedApart()
    {
        var pulls = new TriggerPulls();

        Assert.Equal("a4", pulls.Pull(Axis(L2, 30000)));
        Assert.Equal("a5", pulls.Pull(Axis(R2, 30000)));

        Assert.Null(pulls.Pull(Axis(L2, 31000)));
        Assert.Null(pulls.Pull(Axis(R2, 31000)));
    }

    /// <summary>
    /// Half travel is ZERO here, because the joystick layer runs a trigger from the signed minimum
    /// released to the maximum pressed - not the 0..max the controller layer reports.
    /// </summary>
    [Fact]
    public void HalfTravelIsTheMiddleOfTheSignedRange()
    {
        Assert.Equal(0, TriggerPulls.HalfTravel);

        var pulls = new TriggerPulls();

        // A hair below half is still released.
        Assert.Null(pulls.Pull(Axis(L2, -1)));
        Assert.Null(pulls.Pull(Axis(L2, 0)));
        Assert.Equal("a4", pulls.Pull(Axis(L2, 1)));
    }

    /// <summary>A stick is not a trigger, however far it moves.</summary>
    [Fact]
    public void AStickIsNeverAPull()
    {
        var pulls = new TriggerPulls();

        Assert.Null(pulls.Pull(Axis(LeftStickY, short.MaxValue)));
        Assert.Null(pulls.Pull(Axis(LeftStickY, short.MinValue)));
    }

    /// <summary>And nothing that is not axis motion is one either.</summary>
    [Fact]
    public void OnlyAxisMotionIsConsidered()
        => Assert.Null(new TriggerPulls().Pull(
            new SdlEvent(Gamepads.EventType.JoyButtonDown, 0, 0, R2, 0)));
}
