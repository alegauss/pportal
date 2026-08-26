using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP376: the BIG fragmentation, and the terminator the loop could eat.
///
/// None of this is in PP297's capture: that session's BIG fit in one message, which is the case the
/// defect leaves alone. The interesting sizes are the ones that land exactly on a boundary.
/// </summary>
public class BigFragmentsTests
{
    /// <summary>
    /// THE INVARIANT: whatever the size and whatever the MTU, the plan ends in a fragment that ends
    /// the message - and exactly one fragment does.
    ///
    /// Swept over every payload size across three MTUs rather than over chosen sizes, because the
    /// defect was reachable at exactly one size per MTU and a hand-picked list is how you miss it.
    /// The old condition fails this at total_size == mtu - 25 for every MTU in the sweep.
    /// </summary>
    [Theory]
    [InlineData(526)]   // the narrowest senkusha will measure, less its 50 bytes of network overhead
    [InlineData(1000)]
    [InlineData(1404)]  // the fallback MTU, less the same 50
    public void EveryPlanEndsInATerminator(int mtu)
    {
        for (int size = 1; size <= 2048; size++)
        {
            IReadOnlyList<BigFragment> plan = BigFragments.Plan(size, mtu);

            Assert.NotEmpty(plan);
            Assert.True(
                plan[^1].EndsTheMessage,
                $"the last fragment of a {size}-byte BIG at mtu {mtu} does not end the message");
            Assert.Equal(1, plan.Count(f => f.EndsTheMessage));

            // And the terminator carries something. A zero-length final fragment is what the C's
            // guard was avoiding, and is the reason the loop must leave a strict remainder.
            Assert.True(plan[^1].Size > 0, $"the terminator of a {size}-byte BIG at mtu {mtu} is empty");
        }
    }

    /// <summary>
    /// And every byte is sent exactly once, in order. The fix moved a boundary, so this is what says
    /// it moved the boundary and not the bytes.
    /// </summary>
    [Theory]
    [InlineData(526)]
    [InlineData(1000)]
    [InlineData(1404)]
    public void EveryByteIsSentOnceInOrder(int mtu)
    {
        for (int size = 1; size <= 2048; size++)
        {
            IReadOnlyList<BigFragment> plan = BigFragments.Plan(size, mtu);

            int expected = 0;
            foreach (BigFragment fragment in plan)
            {
                Assert.Equal(expected, fragment.Offset);
                expected += fragment.Size;
            }

            Assert.Equal(size, expected);
        }
    }

    /// <summary>
    /// Only the first fragment is a first message, and only where there is more than one.
    /// </summary>
    [Fact]
    public void OnlyTheFirstFragmentIsAFirstMessage()
    {
        IReadOnlyList<BigFragment> one = BigFragments.Plan(100, 1404);
        Assert.Single(one);
        Assert.True(one[0].IsFirst);
        Assert.True(one[0].EndsTheMessage);

        IReadOnlyList<BigFragment> many = BigFragments.Plan(2048, 526);
        Assert.True(many.Count > 1);
        Assert.True(many[0].IsFirst);
        Assert.All(many.Skip(1), f => Assert.False(f.IsFirst));
    }

    /// <summary>
    /// THE CASE THAT BROKE IT, named on its own so a regression says what it is.
    ///
    /// A remainder of exactly mtu - 25 fits in a continuation. The old condition tested it against
    /// the first-message overhead of 26, found `mtu &lt; mtu + 1`, took a fragment that consumed the
    /// whole remainder, and returned with total_size at 0 - past the guard on the only send that
    /// carries the end-of-message flag.
    /// </summary>
    [Fact]
    public void ARemainderThatExactlyFitsAContinuationStillTerminates()
    {
        const int mtu = 526;

        // One full first fragment, then exactly a continuation's worth left.
        int size = (mtu - BigFragments.FirstOverhead) + (mtu - BigFragments.ContinuationOverhead);

        IReadOnlyList<BigFragment> plan = BigFragments.Plan(size, mtu);

        Assert.Equal(2, plan.Count);
        Assert.True(plan[0].IsFirst);
        Assert.False(plan[0].EndsTheMessage);
        Assert.Equal(mtu - BigFragments.ContinuationOverhead, plan[1].Size);
        Assert.True(plan[1].EndsTheMessage);
    }

    /// <summary>And the C still has the condition and the terminator this reproduces.</summary>
    [Fact]
    public void TheCStillTestsTheRightOverhead()
    {
        string? path = BigFragmentsSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            BigFragmentsSource.TheRemainderIsTestedAgainstTheRightOverhead(core),
            "the fragment loop no longer tests the remainder against the overhead that would carry it");
        Assert.True(
            BigFragmentsSource.TheTerminatorIsStillTheTrailingSend(core),
            "the end-of-message flag moved, so PP376's reasoning about the loop no longer applies");
    }

    /// <summary>
    /// And the reader finds the old condition, so the check above means something.
    /// </summary>
    [Fact]
    public void TheReaderFindsTheOldCondition()
    {
        const string asItWas = "\twhile((mtu < total_size + 26) || (mtu < total_size + 25 && !first))\n";

        Assert.False(BigFragmentsSource.TheRemainderIsTestedAgainstTheRightOverhead(asItWas));
        Assert.False(BigFragmentsSource.TheTerminatorIsStillTheTrailingSend(asItWas));
    }
}
