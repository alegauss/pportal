using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP562: the claim §PP107 opens with, checked - because it had gone the other way.
///
/// Five drift checks watch what the C does and nothing watched what the section SAYS about who runs
/// it. That is the asymmetry this closes: prose does not go red, which is §PP107's own argument for
/// why it wrote those five in the first place.
/// </summary>
public class ReorderQueueSuiteClaimTests
{
    private static string Suite()
    {
        string? path = ReorderQueueSource.LocateSuite();
        Assert.NotNull(path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// THE C SUITE CALLS BOTH, which §PP107 says it never does - in its title and its first
    /// sentence.
    ///
    /// Nothing in the port was wrong because of it: the decision that section reaches rests on not
    /// forking a vendored library, not on nobody running the code. But a deferral is re-read when
    /// the work is picked up again, and it opened with something no longer true.
    /// </summary>
    [Fact]
    public void TheSuiteCallsTheTwoItIsSaidNotTo()
        => Assert.True(ReorderQueueSource.TheSuiteCallsBoth(Suite()));

    /// <summary>
    /// And it pins the drop defect in C: the element is dropped, then asserted still peekable at
    /// the same index with the count unchanged.
    ///
    /// A stronger record than the prose. Repaired upstream, this goes red on the next run rather
    /// than being noticed whenever somebody next reads the section.
    /// </summary>
    [Fact]
    public void TheSuitePinsTheDropDefectInC()
        => Assert.True(ReorderQueueSource.TheSuitePinsTheDropDefect(Suite()));

    /// <summary>
    /// A suite that stopped calling them is caught, which is what would have to happen for the
    /// section's sentence to become true again.
    /// </summary>
    [Fact]
    public void ASuiteThatCallsNeitherIsCaught()
    {
        Assert.False(ReorderQueueSource.TheSuiteCallsBoth("int main(void) { return 0; }"));
        Assert.False(ReorderQueueSource.TheSuitePinsTheDropDefect("int main(void) { return 0; }"));
    }

    /// <summary>
    /// Dropping without the peek that follows it is not pinning it - the assertion has to be the
    /// one that says the element survived.
    /// </summary>
    [Fact]
    public void CallingDropAloneIsNotPinningTheDefect()
    {
        const string touched = "chiaki_reorder_queue_drop(&queue, 2); chiaki_reorder_queue_peek(&q, 0, &s, &u);";

        Assert.True(ReorderQueueSource.TheSuiteCallsBoth(touched));
        Assert.False(ReorderQueueSource.TheSuitePinsTheDropDefect(touched));
    }
}
