using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP93: one client, one answer - and the third answer named rather than used.
/// </summary>
public class TouchpadExtentsTests
{
    /// <summary>The two real pads, which are the only two anything here scales by.</summary>
    [Fact]
    public void TheTwoPadsAreTheRealOnes()
    {
        Assert.Equal(new TouchpadExtents(1920, 942), TouchpadExtents.Ps4);
        Assert.Equal(new TouchpadExtents(1919, 1079), TouchpadExtents.Ps5);
        Assert.Equal(TouchpadExtents.Ps4, TouchpadExtents.For(false));
        Assert.Equal(TouchpadExtents.Ps5, TouchpadExtents.For(true));
    }

    /// <summary>
    /// The third pair is each axis's larger value, so it is not a pad: wider than a DualSense and
    /// taller than a DualShock 4. Stated as arithmetic over the two real ones rather than as the
    /// literal 1920x1079, so the claim survives either pad being corrected.
    /// </summary>
    [Fact]
    public void TheThirdPairIsNeitherPad()
    {
        Assert.Equal(
            new TouchpadExtents(
                Math.Max(TouchpadExtents.Ps4.MaxX, TouchpadExtents.Ps5.MaxX),
                Math.Max(TouchpadExtents.Ps4.MaxY, TouchpadExtents.Ps5.MaxY)),
            TouchpadExtents.QtMacros);

        Assert.NotEqual(TouchpadExtents.Ps4, TouchpadExtents.QtMacros);
        Assert.NotEqual(TouchpadExtents.Ps5, TouchpadExtents.QtMacros);
    }

    /// <summary>
    /// And its error is always OUTWARD, which is why nobody reports it: the gesture overshoots and
    /// stops near the edge instead of stopping short of one the pad really has.
    /// </summary>
    [Fact]
    public void TheThirdPairsErrorIsAlwaysOutward()
    {
        Assert.True(TouchpadExtents.QtMacros.IsOutwardOf(TouchpadExtents.Ps4), "past a DualShock 4");
        Assert.True(TouchpadExtents.QtMacros.IsOutwardOf(TouchpadExtents.Ps5), "past a DualSense");

        // Which axis, on each: a third of the height on a PS4 and one pixel of width on a PS5.
        Assert.Equal(1079 - 942, TouchpadExtents.QtMacros.MaxY - TouchpadExtents.Ps4.MaxY);
        Assert.Equal(1, TouchpadExtents.QtMacros.MaxX - TouchpadExtents.Ps5.MaxX);

        // Neither real pad is outward of the other - each is larger on one axis - so "outward" is
        // a property of the invented pair and not of any pair of pads.
        Assert.False(TouchpadExtents.Ps4.IsOutwardOf(TouchpadExtents.Ps5));
        Assert.False(TouchpadExtents.Ps5.IsOutwardOf(TouchpadExtents.Ps4));
    }

    /// <summary>
    /// chiaki_target_is_ps5 is a THRESHOLD, not a set of known values. A PS5 target this build has
    /// never heard of is still a PS5, and a switch over the enum's members would say otherwise.
    /// </summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4Unknown, false)]
    [InlineData(ChiakiTarget.Ps4_8, false)]
    [InlineData(ChiakiTarget.Ps4_9, false)]
    [InlineData(ChiakiTarget.Ps4_10, false)]
    [InlineData(ChiakiTarget.Ps5Unknown, true)]
    [InlineData(ChiakiTarget.Ps5_1, true)]
    [InlineData((ChiakiTarget)1000200, true)]
    [InlineData((ChiakiTarget)999999, false)]
    public void TheTargetTestIsAThreshold(ChiakiTarget target, bool ps5)
    {
        Assert.Equal(ps5, TouchpadExtents.IsPs5(target));
        Assert.Equal(TouchpadExtents.For(ps5), TouchpadExtents.For(target));
    }

    /// <summary>
    /// Every path agrees, which is the whole of "one answer". The mouse path, the dpad path and the
    /// SDL path are three call sites, and the assertion is that they cannot disagree because the
    /// pair comes from one place.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPathScalesByTheSamePad(bool ps5)
    {
        TouchpadExtents pad = TouchpadExtents.For(ps5);

        (float mouseMaxX, float mouseMaxY) = InputTranslation.TouchpadBounds(ps5);
        Assert.Equal(pad.MaxX, mouseMaxX);
        Assert.Equal(pad.MaxY, mouseMaxY);

        var dpad = new DpadTouch(ps5);
        Assert.Equal(pad.MaxX, dpad.MaxX);
        Assert.Equal(pad.MaxY, dpad.MaxY);

        // And the normalised path, which is the shape the SDL events arrive in.
        Assert.Equal((pad.MaxX, pad.MaxY), pad.Scale(1.0f, 1.0f));
        Assert.Equal(InputTranslation.NormalizedToTouchpad(1.0f, 1.0f, ps5), pad.Scale(1.0f, 1.0f));
        Assert.Equal(InputTranslation.NormalizedToTouchpad(0.5f, 0.5f, ps5), pad.Scale(0.5f, 0.5f));
    }

    /// <summary>Scaling truncates rather than rounding, as the C++ multiplication into a uint16 does.</summary>
    [Theory]
    [InlineData(0.0f, 0)]
    [InlineData(1.0f, 1920)]
    [InlineData(0.5f, 960)]
    [InlineData(0.99999f, 1919)]
    public void ScalingTruncates(float norm, int expectedX)
    {
        Assert.Equal((ushort)expectedX, TouchpadExtents.Ps4.Scale(norm, 0.0f).X);
    }

    /// <summary>
    /// The SDL tracker's extents are a parameter, so a caller cannot get the pad by default. Run
    /// through a real controller state, because the slot bookkeeping is what makes the position
    /// reach the console at all.
    /// </summary>
    [Fact]
    public void TheSdlPathIsHandedThePadRatherThanAssumingOne()
    {
        var tracker = new TouchpadTracker();
        using var state = new ChiakiControllerState();

        Assert.True(tracker.Down(state, 0, 0, 1.0f, 1.0f, TouchpadExtents.Ps4));
        Assert.Equal(TouchpadExtents.Ps4.MaxX, state.Touch(0).X);
        Assert.Equal(TouchpadExtents.Ps4.MaxY, state.Touch(0).Y);

        Assert.True(tracker.Motion(state, 0, 0, 0.0f, 1.0f, TouchpadExtents.Ps5));
        Assert.Equal(0, state.Touch(0).X);
        Assert.Equal(TouchpadExtents.Ps5.MaxY, state.Touch(0).Y);

        Assert.True(tracker.Up(state, 0, 0));
    }

    /// <summary>
    /// The three answers are still three, where the Qt client states them. Asserted as STILL TRUE
    /// rather than fixed: the port diverges from the SDL path deliberately, and a divergence
    /// nobody re-reads is indistinguishable from a mistake.
    /// </summary>
    [Fact]
    public void TheQtClientStillHoldsThreeAnswers()
    {
        string? header = TouchpadExtentsSource.Locate(
            TouchpadExtentsSource.ControllerHeaderRelativePath);
        string? streamSession = TouchpadExtentsSource.Locate(
            TouchpadExtentsSource.StreamSessionRelativePath);
        string? controllerCpp = TouchpadExtentsSource.Locate(
            TouchpadExtentsSource.ControllerCppRelativePath);
        if (header is null || streamSession is null || controllerCpp is null)
            return;

        // One: the macros, and they are the pair this port refuses to scale by.
        Assert.Equal(TouchpadExtents.QtMacros,
            TouchpadExtentsSource.MacroPair(File.ReadAllText(header)));

        // Two and three: the per-console pairs, which are the two this port carries.
        (TouchpadExtents ps4, TouchpadExtents ps5)? pairs =
            TouchpadExtentsSource.PerConsolePairs(File.ReadAllText(streamSession));
        Assert.NotNull(pairs);
        Assert.Equal(TouchpadExtents.Ps4, pairs!.Value.ps4);
        Assert.Equal(TouchpadExtents.Ps5, pairs.Value.ps5);

        // And the SDL path still takes the first of the three, on both the down and the motion.
        Assert.True(
            TouchpadExtentsSource.SdlPathStillUsesTheMacros(File.ReadAllText(controllerCpp)),
            "the SDL touchpad path stopped using PS_TOUCHPAD_MAXX/MAXY");
    }
}
