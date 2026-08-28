using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP473, PP474, PP27: the packets takion holds back until the cipher exists, and who frees them.
///
/// PP449 did the receive thread's timer and PP450 the handshake. This is the thread's other half, and
/// the assertion worth having is about OWNERSHIP: takion_handle_packet's doc comment says it takes the
/// buffer, every branch honours that, and the one that postpones used to lose it on both its failures.
///
/// PP474 fixed all three losses, so these tests hold the repair. The distinctions PP473 drew are kept
/// - held versus dropped is still what a port has to get right.
/// </summary>
public class TakionPostponeTests
{
    private static string? Source()
    {
        string? path = TakionPostpone.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>The first packet makes the array; later ones go into it.</summary>
    [Fact]
    public void TheFirstPacketAllocatesAndTheRestAreBuffered()
    {
        Assert.Equal(
            PostponeOutcome.AllocatedAndBuffered,
            TakionPostpone.Postpone(hasArray: false, count: 0));

        Assert.Equal(PostponeOutcome.Buffered, TakionPostpone.Postpone(hasArray: true, count: 1));
        Assert.Equal(
            PostponeOutcome.Buffered,
            TakionPostpone.Postpone(hasArray: true, count: TakionPostpone.Size - 1));
    }

    /// <summary>
    /// THE ARRAY IS THIRTY-TWO, and the packet after that is dropped.
    ///
    /// Reachable by arithmetic rather than by mishap: a stream sending video before the cipher is
    /// established sends more than thirty-two packets in well under a second.
    /// </summary>
    [Fact]
    public void ThirtyThreeIsOneTooMany()
    {
        Assert.Equal(32, TakionPostpone.Size);

        Assert.Equal(
            PostponeOutcome.NoSpace,
            TakionPostpone.Postpone(hasArray: true, count: TakionPostpone.Size));
    }

    /// <summary>
    /// Two of the four outcomes DROP the packet rather than holding it - which since PP474 is a drop
    /// and not a leak.
    ///
    /// The distinction is kept because it is still what a port has to get right: the caller has let go
    /// of the buffer either way, so whoever does not hold it has to free it.
    /// </summary>
    [Fact]
    public void TwoOutcomesDropThePacket()
    {
        Assert.True(TakionPostpone.BufferIsOwned(PostponeOutcome.AllocatedAndBuffered));
        Assert.True(TakionPostpone.BufferIsOwned(PostponeOutcome.Buffered));

        Assert.False(TakionPostpone.BufferIsOwned(PostponeOutcome.AllocationFailed));
        Assert.False(TakionPostpone.BufferIsOwned(PostponeOutcome.NoSpace));

        Assert.Equal(
            new[] { PostponeOutcome.AllocationFailed, PostponeOutcome.NoSpace },
            TakionPostpone.DropsThePacket.ToArray());
    }

    /// <summary>An allocation that fails drops the packet that triggered it, and frees it.</summary>
    [Fact]
    public void AFailedAllocationDropsItsPacket()
    {
        PostponeOutcome outcome = TakionPostpone.Postpone(
            hasArray: false, count: 0, allocationSucceeds: false);

        Assert.Equal(PostponeOutcome.AllocationFailed, outcome);
        Assert.False(TakionPostpone.BufferIsOwned(outcome));
    }

    /// <summary>
    /// PP474: a cipher that never arrives no longer loses the array, which was the most reachable of
    /// the three - it is what any connect failing before crypt does.
    ///
    /// The parameter is kept and ignored: asking with false is asking the question PP474 removed, and
    /// this is where it gets the new answer.
    /// </summary>
    [Fact]
    public void ACipherThatNeverArrivesStillReleasesTheArray()
    {
        Assert.True(TakionPostpone.ArrayIsReleased(cryptArrived: true));
        Assert.True(TakionPostpone.ArrayIsReleased(cryptArrived: false));
    }

    /// <summary>The size is the C's, read from its define.</summary>
    [Fact]
    public void TheSizeIsStillTheCs()
    {
        if (Source() is not { } source)
            return;

        Assert.Equal((long?)TakionPostpone.Size, TakionPostpone.SizeIn(source));
    }

    /// <summary>
    /// The dispatcher still owns the buffer on every branch but the postpone, which is what makes the
    /// postpone the only place it can be lost.
    /// </summary>
    [Fact]
    public void TheDispatcherOwnsTheBufferEverywhereElse()
    {
        if (Source() is not { } source || TakionPostpone.HandleBody(source) is not { } body)
            return;

        Assert.True(
            TakionPostpone.TheDispatcherStillOwnsTheBuffer(body),
            "takion_handle_packet's frees have changed, so which branch can lose a buffer is no longer "
                + "what this models");
    }

    /// <summary>PP474: both early returns free the buffer before leaving.</summary>
    [Fact]
    public void BothEarlyReturnsFreeTheBuffer()
    {
        if (Source() is not { } source || TakionPostpone.PostponeBody(source) is not { } body)
            return;

        Assert.True(
            TakionPostpone.BothEarlyReturnsFreeTheBuffer(body),
            "one of the two early returns stopped freeing the buffer, so PP474's leak is back");
    }

    /// <summary>PP474: and the array is released on both exits, not only the flush.</summary>
    [Fact]
    public void TheArrayIsReleasedOnBothExits()
    {
        if (Source() is not { } source || TakionPostpone.ThreadBody(source) is not { } body)
            return;

        Assert.True(
            TakionPostpone.TheArrayIsReleasedOnBothExits(body),
            "the array is no longer released on both exits, so a session dying before the cipher leaks it again");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(TakionPostpone.SizeIn(""));
        Assert.Null(TakionPostpone.PostponeBody(""));
        Assert.Null(TakionPostpone.HandleBody(""));
        Assert.False(TakionPostpone.TheDispatcherStillOwnsTheBuffer(""));
        Assert.False(TakionPostpone.BothEarlyReturnsFreeTheBuffer(""));
        Assert.False(TakionPostpone.TheArrayIsReleasedOnBothExits(""));
    }
}
