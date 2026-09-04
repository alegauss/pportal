using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP652: the accumulator between a capture packet and the console's 960-byte unit.
///
/// A capture client hands back the engine's period, not the console's frame. The C announces 480
/// frames of one 16-bit channel, so something holds a remainder across calls, and every way of
/// getting that wrong is silent: a unit assembled from the tail of one packet and the head of the
/// next in the wrong order still has the right length.
///
/// So the packet sizes below are chosen to be awkward rather than representative - smaller than a
/// unit, larger, an exact multiple, a run of odd sizes summing to whole units - and every test
/// checks the CONTENTS and not only the count, because a count is what a wrong order still gets
/// right.
/// </summary>
public class MicrophoneUnitsTests
{
    /// <summary>Bytes numbered from a start, so a wrong order is visible rather than plausible.</summary>
    private static byte[] Run(int from, int length)
        => [.. Enumerable.Range(from, length).Select(i => (byte)(i & 0xff))];

    private static (List<byte[]> Units, MicrophoneUnits Accumulator) Feed(int unitBytes, params int[] packets)
    {
        var units = new List<byte[]>();
        var accumulator = new MicrophoneUnits(unitBytes);

        int at = 0;
        foreach (int size in packets)
        {
            accumulator.Take(Run(at, size), one => units.Add(one.ToArray()));
            at += size;
        }

        return (units, accumulator);
    }

    /// <summary>The announced unit is 960 bytes, which is what the default accumulator is for.</summary>
    [Fact]
    public void TheDefaultUnitIsTheAnnouncedOne()
    {
        var accumulator = new MicrophoneUnits();

        Assert.Equal(960, accumulator.UnitBytes);
        Assert.Equal(MicrophoneFormat.BytesPerUnit(MicrophoneFormat.Announced), accumulator.UnitBytes);
        Assert.Equal(0, accumulator.Pending);
        Assert.Equal(0, accumulator.Emitted);
    }

    /// <summary>A packet smaller than a unit emits nothing and holds all of it.</summary>
    [Fact]
    public void AShortPacketIsHeld()
    {
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(960, 100);

        Assert.Empty(units);
        Assert.Equal(100, accumulator.Pending);
        Assert.Equal(0, accumulator.Emitted);
    }

    /// <summary>An exact unit emits once and holds nothing.</summary>
    [Fact]
    public void AnExactPacketEmitsOnce()
    {
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(960, 960);

        byte[] only = Assert.Single(units);
        Assert.Equal(Run(0, 960), only);
        Assert.Equal(0, accumulator.Pending);
        Assert.Equal(1, accumulator.Emitted);
    }

    /// <summary>
    /// THE ONE THAT MATTERS: a unit spanning two packets is assembled in order.
    ///
    /// 500 then 700 makes one whole unit of 960 and holds 240. The unit's contents are bytes 0 to
    /// 959 in order, which is what a reversed join would get wrong while still being 960 long.
    /// </summary>
    [Fact]
    public void AUnitSpanningTwoPacketsIsInOrder()
    {
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(960, 500, 700);

        byte[] only = Assert.Single(units);
        Assert.Equal(Run(0, 960), only);
        Assert.Equal(240, accumulator.Pending);
        Assert.Equal(1, accumulator.Emitted);
    }

    /// <summary>A packet holding several whole units emits each of them, in order.</summary>
    [Fact]
    public void ALongPacketEmitsEveryWholeUnit()
    {
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(960, 960 * 3 + 17);

        Assert.Equal(3, units.Count);
        Assert.Equal(Run(0, 960), units[0]);
        Assert.Equal(Run(960, 960), units[1]);
        Assert.Equal(Run(1920, 960), units[2]);
        Assert.Equal(17, accumulator.Pending);
    }

    /// <summary>
    /// A run of awkward sizes: every byte comes out once, in order, and none is lost.
    ///
    /// The sum is what makes this a check on the whole stream rather than on one join. Sixteen
    /// packets of shifting size, and the concatenated units have to be the input's prefix exactly.
    /// </summary>
    [Fact]
    public void AwkwardPacketsPreserveEveryByteInOrder()
    {
        int[] sizes = [7, 953, 1, 1919, 480, 480, 3, 2877, 100, 860, 5, 955, 2000, 40, 920, 13];
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(960, sizes);

        int total = sizes.Sum();
        int whole = total / 960;

        Assert.Equal(whole, units.Count);
        Assert.Equal(whole, accumulator.Emitted);
        Assert.Equal(total % 960, accumulator.Pending);

        // Concatenated, the units are the first whole*960 bytes of the stream - which is the whole
        // claim, and the one a per-unit check cannot make.
        Assert.Equal(Run(0, whole * 960), units.SelectMany(one => one).ToArray());
    }

    /// <summary>An empty packet changes nothing, which a capture loop hands over on a silent period.</summary>
    [Fact]
    public void AnEmptyPacketIsNotAUnit()
    {
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(960, 0, 0, 960, 0);

        Assert.Single(units);
        Assert.Equal(0, accumulator.Pending);
    }

    /// <summary>
    /// Reset drops the remainder rather than padding it out.
    ///
    /// A partial unit is not ten milliseconds of audio. Sending one would put the encoder a
    /// fraction of a frame out of step for the rest of the session.
    /// </summary>
    [Fact]
    public void ResetDropsTheRemainderAndDoesNotEmitIt()
    {
        var units = new List<byte[]>();
        var accumulator = new MicrophoneUnits(960);

        accumulator.Take(Run(0, 500), one => units.Add(one.ToArray()));
        Assert.Equal(500, accumulator.Pending);

        accumulator.Reset();

        Assert.Empty(units);
        Assert.Equal(0, accumulator.Pending);
        Assert.Equal(0, accumulator.Emitted);

        // And the next packet starts a unit rather than completing the dropped one.
        accumulator.Take(Run(1000, 960), one => units.Add(one.ToArray()));

        Assert.Equal(Run(1000, 960), Assert.Single(units));
    }

    /// <summary>A unit size below one byte is refused rather than looping forever.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-960)]
    public void AUnitMustHaveASize(int unitBytes)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MicrophoneUnits(unitBytes));

    /// <summary>And a null callback is refused, rather than counting units nobody receives.</summary>
    [Fact]
    public void ANullSinkIsRefused()
    {
        var accumulator = new MicrophoneUnits(960);

        Assert.Throws<ArgumentNullException>(() => accumulator.Take(Run(0, 960), null!));
    }

    /// <summary>
    /// A one-byte unit, which is the degenerate case a loop over lengths can hang on.
    ///
    /// PP272's rule in the other direction: the readers say no about nothing, and this one keeps
    /// its answer finite about the smallest something.
    /// </summary>
    [Fact]
    public void TheSmallestUnitStillTerminates()
    {
        (List<byte[]> units, MicrophoneUnits accumulator) = Feed(1, 5);

        Assert.Equal(5, units.Count);
        Assert.Equal(0, accumulator.Pending);
    }
}
