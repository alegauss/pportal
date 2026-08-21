using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: two generators guessing the same NAT's next port, disagreeing about almost everything.
/// </summary>
public class PortGuessingTests
{
    /// <summary>The spread walks outwards from where it started.</summary>
    [Fact]
    public void TheSpreadIsCentredOnThePortThatAnswered()
    {
        IReadOnlyList<ushort> ports = PortGuessing.Spread(40000, 7);

        Assert.Equal<ushort[]>([40000, 40001, 39999, 40002, 39998, 40003, 39997], [.. ports]);
    }

    /// <summary>Which is what the offsets say, one at a time.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, -1)]
    [InlineData(3, 2)]
    [InlineData(4, -2)]
    [InlineData(74, -37)]
    public void TheOffsetsGoOutwardsAlternating(int index, int delta)
        => Assert.Equal(delta, PortGuessing.Delta(index));

    /// <summary>The steady one walks forwards by whatever the allocation test measured.</summary>
    [Fact]
    public void TheSteadyRunWalksByTheIncrement()
    {
        IReadOnlyList<ushort> ports = PortGuessing.Sequential(40000, 4);

        Assert.Equal<ushort[]>(
            [40004, 40008, 40012, 40016, 40020, 40024, 40028, 40032], [.. ports]);
    }

    /// <summary>
    /// THE STEADY ONE STARTS ONE INCREMENT AHEAD. The port that answered is not among its eight,
    /// because it has already been used - where the spread's first guess IS that port.
    ///
    /// The same observation is either included or excluded depending on which branch was taken.
    /// </summary>
    [Fact]
    public void TheSteadyRunLeavesOutThePortThatAnsweredAndTheSpreadDoesNot()
    {
        Assert.DoesNotContain<ushort>(40000, PortGuessing.Sequential(40000, 4));
        Assert.Equal(40000, PortGuessing.Spread(40000, 4)[0]);
    }

    /// <summary>Eight guesses, and ten candidates in the offer once the other two are added.</summary>
    [Fact]
    public void EightGuessesMakeTenCandidates()
    {
        Assert.Equal(8, PortGuessing.Sequential(40000, 1).Count);
        Assert.Equal(10, PortGuessing.SequentialCandidates);
    }

    /// <summary>And seventy-five guesses caught by two hundred and fifty sockets.</summary>
    [Fact]
    public void TheSpreadDefaultsToSeventyFiveGuesses()
    {
        Assert.Equal(75, PortGuessing.Spread(40000).Count);
        Assert.Equal(250, PortGuessing.RandomAllocationSocks);
    }

    /// <summary>
    /// THEY WRAP THE SAME OVERFLOW TO DIFFERENT PLACES. Past 65535 the steady one lands just above
    /// the well-known range and the spread one lands at the base of the ephemeral range - one
    /// overflow, two answers, about twenty-four thousand apart.
    /// </summary>
    [Fact]
    public void TheSameOverflowLandsInTwoDifferentPlaces()
    {
        // 65535 + 1 by the steady rule.
        Assert.Equal(1025, PortGuessing.Sequential(65535, 1, 1)[0]);

        // The same step by the spread rule.
        Assert.Equal(PortGuessing.EphemeralBase, PortGuessing.Spread(65535, 2)[1]);

        Assert.NotEqual(1025, PortGuessing.EphemeralBase);
    }

    /// <summary>
    /// AND THE STEADY ONE HAS TWO UNDERFLOW RULES, PICKED BY WHERE IT CAME FROM. Coming down from
    /// above 1024 wraps to the top of the space; coming down from within the well-known range adds
    /// 65535 instead.
    /// </summary>
    [Fact]
    public void TheTwoUnderflowRulesAreChosenByWhereItCameFrom()
    {
        // From 1030 with a step of -10: below 1024, and it came from above it.
        Assert.Equal(65531, PortGuessing.Sequential(1030, -10, 1)[0]);

        // From 5 with the same step: below 1, and it came from inside the range.
        Assert.Equal(65530, PortGuessing.Sequential(5, -10, 1)[0]);
    }

    /// <summary>
    /// A guess that walks DOWN THROUGH the well-known ports and stays above zero is left there
    /// rather than wrapped - which the comment calls deliberate: a router already allocating in
    /// that range is a router that uses it.
    /// </summary>
    [Fact]
    public void AWalkInsideTheWellKnownRangeIsLeftAlone()
    {
        IReadOnlyList<ushort> ports = PortGuessing.Sequential(500, -100, 4);

        Assert.Equal<ushort[]>([400, 300, 200, 100], [.. ports]);
    }

    /// <summary>And the spread wraps a low port to the top, with no second rule.</summary>
    [Fact]
    public void TheSpreadHasOneUnderflowRule()
    {
        // 1024 with delta -1 is 1023, which is under the limit.
        IReadOnlyList<ushort> ports = PortGuessing.Spread(1024, 3);

        Assert.Equal<ushort[]>([1024, 1025, 65534], [.. ports]);
    }

    /// <summary>
    /// The steady run's wrap carries on from where it landed rather than restarting, so a run that
    /// crosses the top keeps stepping from the new place.
    /// </summary>
    [Fact]
    public void TheSteadyRunKeepsWalkingAfterItWraps()
    {
        IReadOnlyList<ushort> ports = PortGuessing.Sequential(65530, 4, 4);

        Assert.Equal<ushort[]>([65534, 1027, 1031, 1035], [.. ports]);
    }

    /// <summary>Every port either generator produces is a real port.</summary>
    [Fact]
    public void EveryGuessIsAPortThatExists()
    {
        foreach (ushort port in PortGuessing.Spread(1030))
            Assert.InRange(port, 1, ushort.MaxValue);

        foreach (ushort port in PortGuessing.Sequential(65000, 400, 40))
            Assert.InRange(port, 1, ushort.MaxValue);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheGuessingsRulesAreStillTheQtCores()
    {
        string? path = PortGuessingSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(PortGuessingSource.TheConstantsAreStillTheseValues(core), "seventy-five and two-fifty");
        Assert.True(PortGuessingSource.TheSteadyRunIsStillEight(core), "eight guesses, ten candidates");
        Assert.True(PortGuessingSource.TheSteadyRunStillStartsOneAhead(core), "the add before the assign");
        Assert.True(PortGuessingSource.TheTwoOverflowsStillDisagree(core), "two answers to one overflow");
        Assert.True(PortGuessingSource.TheSteadyRunStillHasTwoUnderflows(core), "two underflow rules");
        Assert.True(
            PortGuessingSource.TheSpreadIsStillCentredAndDuplicated(core), "centred, and written twice");
    }
}
