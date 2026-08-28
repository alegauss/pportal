using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP473, PP27: the packets takion holds back until the cipher exists, and the three ways their
/// buffers are lost.
///
/// PP449 did the receive thread's timer and PP450 the handshake. This is the thread's other half, and
/// the assertion worth having is about OWNERSHIP: takion_handle_packet's own doc comment says it takes
/// the buffer, every branch honours that, and the one that postpones loses it on both its failures.
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
    /// THE FINDING: two of the four outcomes lose the buffer, because the caller has already let go of
    /// it.
    ///
    /// A drop would be fine. This is a leak - takion_handle_packet's doc comment says ownership is
    /// taken, and both early returns in postpone return without freeing.
    /// </summary>
    [Fact]
    public void TwoOutcomesLoseTheBuffer()
    {
        Assert.True(TakionPostpone.BufferIsOwned(PostponeOutcome.AllocatedAndBuffered));
        Assert.True(TakionPostpone.BufferIsOwned(PostponeOutcome.Buffered));

        Assert.False(TakionPostpone.BufferIsOwned(PostponeOutcome.AllocationFailed));
        Assert.False(TakionPostpone.BufferIsOwned(PostponeOutcome.NoSpace));

        Assert.Equal(
            new[] { PostponeOutcome.AllocationFailed, PostponeOutcome.NoSpace },
            TakionPostpone.LosesTheBuffer.ToArray());
    }

    /// <summary>An allocation that fails loses the packet that triggered it.</summary>
    [Fact]
    public void AFailedAllocationLosesItsPacket()
    {
        PostponeOutcome outcome = TakionPostpone.Postpone(
            hasArray: false, count: 0, allocationSucceeds: false);

        Assert.Equal(PostponeOutcome.AllocationFailed, outcome);
        Assert.False(TakionPostpone.BufferIsOwned(outcome));
    }

    /// <summary>
    /// AND A CIPHER THAT NEVER ARRIVES LOSES EVERY ONE, which is the most reachable of the three: it is
    /// what any connect that fails before crypt does.
    /// </summary>
    [Fact]
    public void ACipherThatNeverArrivesLosesTheWholeArray()
    {
        Assert.True(TakionPostpone.ArrayIsReleased(cryptArrived: true));
        Assert.False(TakionPostpone.ArrayIsReleased(cryptArrived: false));
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

    /// <summary>Both early returns still leave without freeing.</summary>
    [Fact]
    public void BothEarlyReturnsStillLeak()
    {
        if (Source() is not { } source || TakionPostpone.PostponeBody(source) is not { } body)
            return;

        Assert.True(
            TakionPostpone.BothEarlyReturnsStillLeak(body),
            "one of the two early returns now frees the buffer, so the filed fix has partly landed and "
                + "this model is behind the C");
    }

    /// <summary>And the flush is still the only thing that releases the array.</summary>
    [Fact]
    public void OnlyTheFlushReleasesTheArray()
    {
        if (Source() is not { } source || TakionPostpone.ThreadBody(source) is not { } body)
            return;

        Assert.True(
            TakionPostpone.OnlyTheFlushReleasesTheArray(body),
            "the array is freed somewhere else now, or the flush stopped being guarded on the cipher");
    }

    /// <summary>PP272: and the readers say no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
    {
        Assert.Null(TakionPostpone.SizeIn(""));
        Assert.Null(TakionPostpone.PostponeBody(""));
        Assert.Null(TakionPostpone.HandleBody(""));
        Assert.False(TakionPostpone.TheDispatcherStillOwnsTheBuffer(""));
        Assert.False(TakionPostpone.BothEarlyReturnsStillLeak(""));
        Assert.False(TakionPostpone.OnlyTheFlushReleasesTheArray(""));
    }
}
