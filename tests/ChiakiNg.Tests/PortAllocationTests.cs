using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the widest decision tree in the hole punching, driven rather than read.
/// </summary>
public class PortAllocationTests
{
    private const string A = "203.0.113.1";
    private const string B = "203.0.113.2";
    private const string C = "203.0.113.3";
    private const string D = "203.0.113.4";

    private static StunResponse R(string address, int port) => new(address, (ushort)port);

    private static PortAllocation Test(params StunResponse[] responses)
    {
        PortAllocation? result = PortAllocationTest.Test(responses);
        Assert.NotNull(result);
        return result.Value;
    }

    /// <summary>Nothing answered is the one case that fails rather than guessing.</summary>
    [Fact]
    public void NoAnswersIsAFailureAndNotAZeroIncrement()
        => Assert.Null(PortAllocationTest.Test([]));

    /// <summary>One answer says nothing about movement, so the port is offered untouched.</summary>
    [Fact]
    public void OneAnswerOffersItselfWithNoIncrement()
    {
        PortAllocation result = Test(R(A, 1000));

        Assert.Equal(A, result.Address);
        Assert.Equal(1000, result.Port);
        Assert.Equal(0, result.Increment);
        Assert.False(result.RandomAllocation);
    }

    /// <summary>
    /// THE LOOK-AHEAD SHRINKS AS THE EVIDENCE GROWS. Two answers offer TWO increments ahead, three
    /// offer ONE, and four offer NONE - so the case that knows the most about the NAT is the only
    /// one that does not use what it learned.
    ///
    /// A port that completed the pattern would offer a different port than the Qt client on exactly
    /// the networks where the measurement worked best.
    /// </summary>
    [Fact]
    public void TheLookAheadShrinksFromTwoToOneToNone()
    {
        // Two: 2000 + 2 * 1000.
        PortAllocation two = Test(R(A, 1000), R(A, 2000));
        Assert.Equal(1000, two.Increment);
        Assert.Equal(4000, two.Port);

        // Three: 3000 + 1000.
        PortAllocation three = Test(R(A, 1000), R(A, 2000), R(A, 3000));
        Assert.Equal(1000, three.Increment);
        Assert.Equal(4000, three.Port);

        // Four: 4000, and not 5000.
        PortAllocation four = Test(R(A, 1000), R(A, 2000), R(A, 3000), R(A, 4000));
        Assert.Equal(1000, four.Increment);
        Assert.Equal(4000, four.Port);
    }

    /// <summary>An address that changed between two answers means no measurement at all.</summary>
    [Fact]
    public void TwoAnswersFromDifferentAddressesMeasureNothing()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 2000));

        Assert.Equal(B, result.Address);
        Assert.Equal(0, result.Increment);
        Assert.Equal(2000, result.Port);
    }

    /// <summary>Three answers, first two differing: the increment is halved over the pair that agrees.</summary>
    [Fact]
    public void ThreeAnswersWhereTheFirstAndThirdAgreeHalveTheGap()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 5000), R(A, 3000));

        Assert.Equal(A, result.Address);
        Assert.Equal(1000, result.Increment);
        Assert.Equal(4000, result.Port);
    }

    /// <summary>And where only the last two agree, it is the plain difference of those two.</summary>
    [Fact]
    public void ThreeAnswersWhereTheLastTwoAgreeUseTheirGap()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 2000), R(B, 2500));

        Assert.Equal(500, result.Increment);
        Assert.Equal(3000, result.Port);
    }

    /// <summary>Three different addresses in three answers measure nothing.</summary>
    [Fact]
    public void ThreeDifferentAddressesMeasureNothing()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 2000), R(C, 3000));

        Assert.Equal(C, result.Address);
        Assert.Equal(0, result.Increment);
        Assert.Equal(3000, result.Port);
    }

    /// <summary>
    /// Two ways of calculating the same increment, disagreeing, is what RANDOM ALLOCATION means -
    /// and the second figure is only adopted when the first came out zero, so a measured increment
    /// is never overwritten by a second opinion.
    /// </summary>
    [Fact]
    public void TwoDisagreeingCalculationsMeanRandomAllocation()
    {
        // 2000-1000 is a thousand; 3500-2000 is fifteen hundred. They disagree.
        PortAllocation result = Test(R(A, 1000), R(A, 2000), R(A, 3500));

        Assert.True(result.RandomAllocation);
        Assert.Equal(1000, result.Increment);
    }

    /// <summary>And where the first came out zero, the second opinion is the one that stands.</summary>
    [Fact]
    public void AZeroFirstOpinionIsReplacedByTheSecond()
    {
        // 1000-1000 is zero; 2500-1000 is fifteen hundred.
        PortAllocation result = Test(R(A, 1000), R(A, 1000), R(A, 2500));

        Assert.True(result.RandomAllocation);
        Assert.Equal(1500, result.Increment);
    }

    /// <summary>
    /// ONE LEAF SUBTRACTS THE WRONG WAY ROUND. Where the first two addresses differ, the second
    /// matches neither of the last two, and the last two match each other, the increment is
    /// port3 - port4 - and every other leaf in the tree is later-minus-earlier.
    ///
    /// On a NAT whose ports climb, that leaf alone produces a NEGATIVE increment. Reproduced,
    /// because it is a real number this client puts in a real offer.
    /// </summary>
    [Fact]
    public void TheOneBackwardsLeafProducesANegativeIncrement()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 2000), R(C, 3000), R(C, 3500));

        Assert.Equal(-500, result.Increment);
        Assert.Equal(C, result.Address);
        Assert.Equal(3500, result.Port);
    }

    /// <summary>And every neighbouring leaf gets the sign right, which is what makes it the odd one.</summary>
    [Fact]
    public void TheNeighbouringLeavesGetTheSignRight()
    {
        // The same shape, but with the FIRST address matching the last two.
        PortAllocation forwards = Test(R(A, 1000), R(B, 2000), R(A, 3000), R(A, 3500));

        Assert.Equal(500, forwards.Increment);
    }

    /// <summary>
    /// THE SAME SUBTRACTION IS UNSIGNED IN ONE LEAF. Where all four addresses agree the increments
    /// are sixteen-bit unsigned, so a port that went DOWN wraps to about sixty-five thousand -
    /// where the very same difference is simply negative everywhere else in the tree.
    /// </summary>
    [Fact]
    public void TheAgreeingLeafWrapsWhereTheOthersGoNegative()
    {
        // All four the same address, ports falling by five hundred each time.
        PortAllocation wrapped = Test(R(A, 4000), R(A, 3500), R(A, 3000), R(A, 2500));

        Assert.Equal(ushort.MaxValue - 499, wrapped.Increment);
        Assert.False(wrapped.RandomAllocation);

        // The same falling ports where the addresses do NOT all agree stay negative.
        PortAllocation negative = Test(R(A, 4000), R(A, 3500), R(B, 3000), R(B, 2500));

        Assert.Equal(-500, negative.Increment);
    }

    /// <summary>
    /// AVERAGES ARE TAKEN ACROSS AN ADDRESS THE BRANCH JUST FOUND DIFFERENT. This leaf divides by
    /// four over port1, in a branch reached only because addr1 did not match addr2 - so a
    /// measurement of a different external address is averaged in anyway.
    /// </summary>
    [Fact]
    public void TheQuarterAverageCrossesAnAddressItFoundDifferent()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 9999), R(C, 8888), R(B, 5000));

        Assert.Equal(1000, result.Increment);
        Assert.Equal(B, result.Address);
    }

    /// <summary>Two of three increments agreeing is enough, and the odd one out is discarded.</summary>
    [Fact]
    public void TwoOfThreeAgreeingIncrementsWin()
    {
        PortAllocation result = Test(R(A, 1000), R(A, 2000), R(A, 3000), R(A, 9000));

        Assert.Equal(1000, result.Increment);
        Assert.False(result.RandomAllocation);
    }

    /// <summary>And the LAST two agreeing works too, which is a separate branch.</summary>
    [Fact]
    public void TheLastTwoAgreeingIncrementsWinAsWell()
    {
        PortAllocation result = Test(R(A, 1000), R(A, 9000), R(A, 10000), R(A, 11000));

        Assert.Equal(1000, result.Increment);
        Assert.False(result.RandomAllocation);
    }

    /// <summary>Three increments that all disagree is random allocation, taking the first non-zero.</summary>
    [Fact]
    public void ThreeDisagreeingIncrementsAreRandomAllocation()
    {
        PortAllocation result = Test(R(A, 1000), R(A, 2000), R(A, 4000), R(A, 7000));

        Assert.True(result.RandomAllocation);
        Assert.Equal(1000, result.Increment);
    }

    /// <summary>And where the first increment is zero, the second is taken instead.</summary>
    [Fact]
    public void AZeroFirstIncrementFallsThroughToTheSecond()
    {
        PortAllocation result = Test(R(A, 1000), R(A, 1000), R(A, 3000), R(A, 6000));

        Assert.True(result.RandomAllocation);
        Assert.Equal(2000, result.Increment);
    }

    /// <summary>Four different addresses measure nothing, and the fourth is the one offered.</summary>
    [Fact]
    public void FourDifferentAddressesMeasureNothing()
    {
        PortAllocation result = Test(R(A, 1000), R(B, 2000), R(C, 3000), R(D, 4000));

        Assert.Equal(D, result.Address);
        Assert.Equal(0, result.Increment);
        Assert.Equal(4000, result.Port);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheTreesRulesAreStillTheQtCores()
    {
        string? path = PortAllocationSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(PortAllocationSource.TheLookAheadStillShrinks(core), "two, one, none");
        Assert.True(PortAllocationSource.TheBackwardsLeafIsStillBackwards(core), "port3 - port4");
        Assert.True(PortAllocationSource.TheAgreeingLeafIsStillUnsigned(core), "three uint16_t");
        Assert.True(
            PortAllocationSource.TheAveragesStillCrossAnAddressChange(core), "averaged across a change");
        Assert.True(
            PortAllocationSource.TheSecondOpinionStillOnlyFillsAZero(core), "a second opinion fills a zero");
    }

    /// <summary>
    /// AND THE SHUFFLE IS WRITTEN TWICE. PP198 pinned one spelling; this header contains a second
    /// copy of the same broken draw with the operands the other way round, so that check covered
    /// half the file it was about.
    /// </summary>
    [Fact]
    public void TheBrokenShuffleAppearsTwiceInTheSameHeader()
    {
        string? path = PortAllocationSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(PortAllocationSource.TheShuffleIsStillWrittenTwice(core));
    }
}
