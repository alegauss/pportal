using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP126: test/reorderqueue.c's sequence, which closes PP35's last module.
///
/// Unlike the five that came before it, this one is mostly granularity rather than new coverage:
/// the host's selftest already drives the window size, the head-missing case, in-order release,
/// both overflow strategies and PP107's accepted defects. What it does not is the SEQUENCE - the
/// C walks one queue through empty, one, empty, outdated, and a filling out-of-order run, and the
/// state each step leaves is the input to the next.
///
/// So this is written as one walk rather than as independent facts, which is the shape that can
/// fail where the selftest cannot: a queue correct on every operation taken alone and wrong about
/// what one leaves behind for the next.
/// </summary>
public class ReorderQueueTests
{
    /// <summary>2^2 is four slots, starting at sequence number 42 - the C's own parameters.</summary>
    private static ReorderQueue NewQueue() => new(2, 42);

    [Fact]
    public void PullingFromEmptyIsRefusedAndDropsNothing()
    {
        using ReorderQueue queue = NewQueue();

        Assert.Equal(4, queue.Size);
        Assert.Equal(0UL, queue.Count);

        // Refused, not an exception and not a phantom packet. The receive loop pulls until it
        // gets nothing, so this is the ordinary case and it runs on every packet.
        Assert.Null(queue.Pull());
        Assert.Equal(0UL, queue.Count);
        Assert.Empty(queue.Drops);
    }

    /// <summary>
    /// The whole walk, in the C's order, with the state after each step asserted.
    ///
    /// The step that matters is the fourth: pushing 42 AGAIN after it has been pulled. The window
    /// has moved past it, so it is not a duplicate to be held - it is outdated, and it goes to the
    /// drop callback. A queue that kept it would deliver a packet twice, and one that ignored it
    /// silently would lose the drop report the congestion path counts.
    /// </summary>
    [Fact]
    public void TheWalkLeavesTheStateEachStepExpects()
    {
        using ReorderQueue queue = NewQueue();

        Assert.Null(queue.Pull());

        queue.Push(42, 0);
        Assert.Equal(1UL, queue.Count);

        Assert.Equal((42UL, 0L), queue.Pull());
        Assert.Equal(0UL, queue.Count);
        Assert.Empty(queue.Drops);

        // Outdated: the window has moved past 42 and will not take it back.
        queue.Push(42, 0);
        Assert.Equal(0UL, queue.Count);
        Assert.Equal([new ReorderDrop(42, 0)], queue.Drops);
    }

    /// <summary>
    /// Filling out of order, pulling in between, and getting nothing until the head arrives.
    ///
    /// The queue has to be walked into this state rather than started in it, and that is the
    /// point of writing it as a walk. A window of four starting at 42 cannot hold 46 at all -
    /// 42 through 46 is five - so 46 only fits once 42 has been pulled and the begin has moved
    /// to 43. Starting fresh and pushing 46 drops it, which is what the first version of this
    /// test asserted against and was wrong about.
    /// </summary>
    [Fact]
    public void TheWindowSpansToTheNewestAndHoldsUntilTheHeadArrives()
    {
        using ReorderQueue queue = NewQueue();

        // Walk the begin to 43, as the C's sequence does.
        queue.Push(42, 0);
        Assert.Equal((42UL, 0L), queue.Pull());

        queue.Push(46, 1);
        Assert.Null(queue.Pull());

        queue.Push(45, 2);
        Assert.Null(queue.Pull());

        queue.Push(44, 3);
        Assert.Null(queue.Pull());

        // 43 is the head. Its arrival releases the run, in order, and not before.
        queue.Push(43, 4);

        Assert.Equal((43UL, 4L), queue.Pull());
        Assert.Equal((44UL, 3L), queue.Pull());
        Assert.Equal((45UL, 2L), queue.Pull());
        Assert.Equal((46UL, 1L), queue.Pull());
        Assert.Null(queue.Pull());
    }

    /// <summary>
    /// And the count while that run is held is the SPAN, not the population: one packet at 46
    /// with the begin at 43 is a count of four, three of which have not arrived.
    ///
    /// This is the part a rewrite gets wrong by reading count as "how many I have", which is
    /// what PP108 established - and it is why the pull above returns nothing while count is 4.
    /// </summary>
    [Fact]
    public void TheCountIsTheSpanAndNotThePopulation()
    {
        using ReorderQueue queue = NewQueue();

        queue.Push(42, 0);
        queue.Pull();

        queue.Push(46, 1);

        Assert.Equal(4UL, queue.Count);
        Assert.Null(queue.Pull());
    }

    /// <summary>
    /// And the window really is that tight: from a fresh queue, 46 is out of reach and dropped
    /// rather than held. Without this the walk above reads as arbitrary rather than as forced.
    /// </summary>
    [Fact]
    public void AFreshQueueCannotReachThatFar()
    {
        using ReorderQueue queue = NewQueue();

        queue.Push(46, 1);

        Assert.Equal(0UL, queue.Count);
        Assert.Equal([new ReorderDrop(46, 1)], queue.Drops);
    }
}
