using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP220: where an axis rested, which is the only thing that tells noise from a stick off centre.
///
/// A token cannot answer it. SDL raises an axis event whenever the value CHANGES - by one or by
/// twenty thousand - so a flood of `a1` proves the stick is not still and says nothing about where
/// it is. Measured on a DualSense: 1684 tokens in twenty seconds with the analog opt-in on, one
/// unbroken run of 798 from the left stick's Y axis alone.
/// </summary>
public class AxisRangesTests
{
    private static SdlEvent Axis(byte index, short value)
        => new(Gamepads.EventType.JoyAxisMotion, 0, 0, index, 0, value);

    private static SdlEvent Button(byte index)
        => new(Gamepads.EventType.JoyButtonDown, 0, 0, index, 0);

    /// <summary>Anything that is not axis motion is ignored, so the whole stream can be offered.</summary>
    [Fact]
    public void OnlyAxisMotionCounts()
    {
        var ranges = new AxisRanges();

        ranges.Observe(Button(0));
        ranges.Observe(new SdlEvent(Gamepads.EventType.JoyHatMotion, 0, 0, 0, 1));

        Assert.Empty(ranges.Seen());
    }

    /// <summary>Each axis keeps its own low, high and count.</summary>
    [Fact]
    public void EachAxisKeepsItsOwnRange()
    {
        var ranges = new AxisRanges();

        ranges.Observe(Axis(1, -300));
        ranges.Observe(Axis(1, 240));
        ranges.Observe(Axis(1, -80));
        ranges.Observe(Axis(0, 12000));

        Assert.Equal(
            [("a0", (short)12000, (short)12000, 1), ("a1", (short)-300, (short)240, 3)],
            ranges.Seen());
    }

    /// <summary>
    /// The reading is SIGNED. Left and down are negative on a stick, and an unsigned read would
    /// turn every one of them into a number near full scale and make a centred stick look pinned.
    /// </summary>
    [Fact]
    public void TheReadingIsSigned()
    {
        var ranges = new AxisRanges();
        ranges.Observe(Axis(1, short.MinValue));

        (string axis, short low, short high, _) = ranges.Seen()[0];

        Assert.Equal("a1", axis);
        Assert.Equal(short.MinValue, low);
        Assert.Equal(short.MinValue, high);
        Assert.True(low < 0);
    }

    /// <summary>
    /// The extent is the furthest from centre either way, as a fraction of full scale - which is
    /// what makes a hundred noisy samples and one real deflection comparable.
    /// </summary>
    [Fact]
    public void TheExtentIsTheFurthestFromCentreEitherWay()
    {
        Assert.Equal(0.0, AxisRanges.Extent(0, 0));

        // Ordinary noise around centre: a fraction of one percent.
        Assert.True(AxisRanges.Extent(-300, 240) < 0.01);

        // And a stick actually pushed.
        Assert.True(AxisRanges.Extent(-100, 30000) > 0.9);

        // The negative side counts as much as the positive one.
        Assert.Equal(AxisRanges.Extent(-16384, 0), AxisRanges.Extent(0, 16384));
    }

    /// <summary>The line a person reads, carrying the range, the fraction and the sample count.</summary>
    [Fact]
    public void TheLineCarriesTheRangeAndTheCount()
    {
        string line = CaptureReport.AxisRange("a1", -300, 240, 798);

        Assert.Contains("a1", line, StringComparison.Ordinal);
        Assert.Contains("-300..240", line, StringComparison.Ordinal);
        Assert.Contains("798 sample(s)", line, StringComparison.Ordinal);
    }

    /// <summary>Full scale is the event field's own, not a number chosen here.</summary>
    [Fact]
    public void FullScaleIsTheFieldsOwn()
        => Assert.Equal(-(double)short.MinValue, AxisRanges.FullScale);
}
