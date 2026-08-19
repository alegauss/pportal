using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP125: takion's send buffer, the last of test/takion.c's four cases the port did not drive.
///
/// The C's version is randomised - it fills the buffer with random sequence numbers, acks at
/// random offsets and checks what remains after each. Randomness is right there and wrong here:
/// a failure that only reproduces under one seed is a failure nobody can act on, and this suite
/// has no seed to print. So the same PROPERTIES are asserted over chosen values, including the
/// two the random walk covers only by luck - the wrap, and an ack for a packet never sent.
/// </summary>
public class SendBufferTests
{
    private const int Size = 0x30;

    [Fact]
    public void ItFillsToItsSizeAndThenRefuses()
    {
        using var buffer = new SendBuffer(Size);

        for (uint i = 0; i < Size; i++)
            Assert.Equal(ChiakiError.Success, buffer.Push(1000 + i, 8));

        Assert.Equal(Size, buffer.Count);

        // OVERFLOW, not an exception: the buffer is saying the console is behind, which a caller
        // has to handle. A push that threw here would turn a slow network into a crash.
        Assert.Equal(ChiakiError.Overflow, buffer.Push(9999, 8));
        Assert.Equal(Size, buffer.Count);
    }

    /// <summary>
    /// An ack releases that packet and every older one - not just the one named. This is the
    /// property the whole module exists for, and getting it wrong in either direction is silent.
    /// </summary>
    [Fact]
    public void AnAckReleasesEverythingOlderAsWell()
    {
        using var buffer = new SendBuffer(Size);

        for (uint i = 0; i < 10; i++)
            buffer.Push(1000 + i, 8);

        buffer.Ack(1004);

        // Five left, which is the observable: which five is not askable across the seam, because
        // the packet struct is private to takionsendbuffer.c. The count carries the property.
        Assert.Equal(5, buffer.Count);
    }

    /// <summary>
    /// And releasing them frees the room they held, so the buffer can be filled again. Without
    /// this an ack that removed the entries but not the space would look correct by count and
    /// still stop the session at the next push.
    /// </summary>
    [Fact]
    public void ReleasedRoomCanBeUsedAgain()
    {
        using var buffer = new SendBuffer(Size);

        for (uint i = 0; i < Size; i++)
            buffer.Push(1000 + i, 8);

        Assert.Equal(ChiakiError.Overflow, buffer.Push(2000, 8));

        buffer.Ack(1000 + Size - 1);
        Assert.Equal(0, buffer.Count);
        Assert.Equal(ChiakiError.Success, buffer.Push(2000, 8));
    }

    /// <summary>
    /// An ack for a number older than anything held changes nothing. The random walk reaches this
    /// only by chance - it acks at offsets around the newest - and a rewrite that treated "not
    /// found" as "release everything" would pass every ordered case and empty the buffer here.
    /// </summary>
    [Fact]
    public void AnAckOlderThanEverythingHeldReleasesNothing()
    {
        using var buffer = new SendBuffer(Size);

        for (uint i = 0; i < 5; i++)
            buffer.Push(1000 + i, 8);

        buffer.Ack(500);
        Assert.Equal(5, buffer.Count);
    }

    /// <summary>
    /// The one that makes this module hard: "older" is RFC 1982 serial comparison, not integer
    /// order. Around the wrap, 0xffffffff is OLDER than 5 - so acking 5 must release it, and a
    /// buffer comparing with &lt; keeps it forever and fills up.
    /// </summary>
    [Fact]
    public void OlderIsSerialOrderAndNotIntegerOrder()
    {
        using var buffer = new SendBuffer(Size);

        uint[] acrossTheWrap = [0xfffffffd, 0xfffffffe, 0xffffffff, 0, 1, 2];
        foreach (uint n in acrossTheWrap)
            buffer.Push(n, 8);

        Assert.Equal(6, buffer.Count);

        // 0 is newer than 0xffffffff by serial order, so this releases the four up to and
        // including it, and leaves 1 and 2. A buffer comparing with < releases nothing at all.
        buffer.Ack(0);

        Assert.Equal(2, buffer.Count);
    }

    /// <summary>
    /// Nothing is released before it is acked. Trivially true and worth one assertion, because
    /// every test above measures what an ack REMOVES and none of them would notice a buffer that
    /// dropped packets on push.
    /// </summary>
    [Fact]
    public void NothingLeavesTheBufferUntilItIsAcked()
    {
        using var buffer = new SendBuffer(Size);

        for (uint i = 0; i < 20; i++)
        {
            buffer.Push(1000 + i, 8);
            Assert.Equal((int)i + 1, buffer.Count);
        }
    }
}
