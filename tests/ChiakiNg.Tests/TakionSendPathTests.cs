using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP675: chiaki_takion_send - the MAC and the socket, both under one lock.
///
/// The C holds gkcrypt_local_mutex across the stamp AND the send. Two threads that stamped under a
/// lock and then raced to the socket would put their datagrams on the wire in an order neither key
/// position matches, and a console reads that position out of the packet - so a stream cipher fed
/// positions out of order produces noise rather than an error, which is the failure nothing reports.
///
/// SO THE ORDERING IS WHAT THESE HOLD, and it is only observable to something watching both ends.
/// RecordingTakionWire runs a hook at the moment of the send, so a test can ask whether the lock was
/// still held when the datagram left.
/// </summary>
public class TakionSendPathTests(ITestOutputHelper output)
{
    private static byte[] Datagram(int size = 32)
    {
        var datagram = new byte[size];
        datagram[0] = TakionMessageHeader.ControlPacketType;

        for (int i = 1; i < size; i++)
            datagram[i] = (byte)(i + 0x50);

        return datagram;
    }

    /// <summary>THE STAMP REACHES THE WIRE: what is sent carries the MAC, not what was passed in.</summary>
    [Fact]
    public void TheDatagramTheWireGetsIsTheStampedOne()
    {
        byte[] datagram = Datagram();
        var wire = new RecordingTakionWire();
        var cipherLock = new object();

        ChiakiError sent = TakionSendPath.Send(
            datagram, _ => [0xDE, 0xAD, 0xBE, 0xEF], wire, cipherLock);

        Assert.Equal(ChiakiError.Success, sent);

        byte[] onTheWire = Assert.Single(wire.Sent);

        // The MAC field for a control packet, carrying what the cipher produced.
        Assert.Equal(
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            onTheWire.AsSpan(1 + TakionMessageHeader.MacOffset, 4).ToArray());

        // And the caller's buffer was mutated, which is the C's behaviour and the reason it is a Span.
        Assert.Equal(onTheWire, datagram);
    }

    /// <summary>
    /// THE SEND IS INSIDE THE LOCK, asked of the lock itself at the moment of the send.
    ///
    /// A second thread tries to take the same lock while the wire's hook is running. It must not
    /// get it - which is exactly the claim "the send is inside" makes, and one a test that only
    /// checked the stamp could not make at all.
    /// </summary>
    [Fact]
    public void TheWireIsCalledWithTheLockStillHeld()
    {
        var cipherLock = new object();
        var wire = new RecordingTakionWire();
        bool takenByAnother = false;

        wire.OnSend = _ =>
        {
            var other = new Thread(() =>
            {
                if (Monitor.TryEnter(cipherLock, TimeSpan.FromMilliseconds(200)))
                {
                    takenByAnother = true;
                    Monitor.Exit(cipherLock);
                }
            });

            other.Start();
            other.Join(TimeSpan.FromSeconds(5));
        };

        TakionSendPath.Send(Datagram(), null, wire, cipherLock);

        Assert.Single(wire.Sent);
        Assert.False(takenByAnother, "another thread took the cipher lock while a datagram was being sent");
    }

    /// <summary>
    /// And the lock is RECURSIVE for its holder, which the C asks for explicitly.
    ///
    /// chiaki_mutex_init(&amp;takion->gkcrypt_local_mutex, true). PP676's feedback and microphone sends
    /// advance the key position and then send while still holding it, so a path that owned a
    /// private lock would deadlock them. Held here by sending from inside the lock.
    /// </summary>
    [Fact]
    public void ACallerAlreadyHoldingTheLockIsNotDeadlocked()
    {
        var cipherLock = new object();
        var wire = new RecordingTakionWire();

        lock (cipherLock)
        {
            ChiakiError sent = TakionSendPath.Send(Datagram(), null, wire, cipherLock);

            Assert.Equal(ChiakiError.Success, sent);
        }

        Assert.Single(wire.Sent);
    }

    /// <summary>
    /// With no cipher the field is still blanked, and NOTHING is allocated.
    ///
    /// Every send before crypt exists takes this path, and the C blanks unconditionally ahead of the
    /// cipher test. The allocation claim is measured rather than asserted about: a thousand sends
    /// through a wire that copies nothing, with the collector's total read before and after.
    /// </summary>
    [Fact]
    public void WithNoCipherTheFieldIsBlankedAndNothingIsAllocated()
    {
        byte[] datagram = Datagram();

        for (int i = 0; i < 4; i++)
            datagram[1 + TakionMessageHeader.MacOffset + i] = 0xFF;

        var counting = new CountingWire();
        var cipherLock = new object();

        // Warm the path so the first call's JIT is not charged to it.
        TakionSendPath.Send(datagram, null, counting, cipherLock);

        Assert.Equal(
            new byte[4],
            datagram.AsSpan(1 + TakionMessageHeader.MacOffset, 4).ToArray());

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
            TakionSendPath.Send(datagram, null, counting, cipherLock);

        long after = GC.GetAllocatedBytesForCurrentThread();

        output.WriteLine($"{after - before} bytes over 1000 sends");

        Assert.Equal(0, after - before);
        Assert.Equal(1001, counting.Count);
    }

    /// <summary>A wire that counts and copies nothing, so the measurement above is the path's.</summary>
    private sealed class CountingWire : ITakionWire
    {
        public int Count { get; private set; }

        public ChiakiError Send(ReadOnlySpan<byte> datagram)
        {
            Count++;
            return ChiakiError.Success;
        }
    }

    /// <summary>A refused stamp does not reach the wire, which is the C's early return.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void ARefusedStampSendsNothing(int size)
    {
        var wire = new RecordingTakionWire();

        ChiakiError sent = TakionSendPath.Send(new byte[size], null, wire, new object());

        Assert.NotEqual(ChiakiError.Success, sent);
        Assert.Empty(wire.Sent);
    }

    /// <summary>And the wire's own failure comes back, rather than being swallowed.</summary>
    [Fact]
    public void TheWiresFailureIsReturned()
    {
        var wire = new RecordingTakionWire { Result = ChiakiError.Network };

        Assert.Equal(
            ChiakiError.Network,
            TakionSendPath.Send(Datagram(), null, wire, new object()));

        Assert.Single(wire.Sent);
    }

    /// <summary>A null wire or lock is refused rather than sending nowhere.</summary>
    [Fact]
    public void ANullWireOrLockIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => TakionSendPath.Send(Datagram(), null, null!, new object()));

        Assert.Throws<ArgumentNullException>(
            () => TakionSendPath.Send(Datagram(), null, new RecordingTakionWire(), null!));
    }

    /// <summary>
    /// THE JOIN: a built datagram goes through the path and comes back off the wire parseable.
    ///
    /// The builder, the stamp and the send in one line, which is what PP675 owes as a whole rather
    /// than as three parts that were each tested alone.
    /// </summary>
    [Fact]
    public void ABuiltAckGoesThroughThePathAndParsesOffTheWire()
    {
        const uint tagRemote = 0x71DC1006;

        byte[] datagram = new byte[TakionDataDatagrams.AckSize];
        TakionDataDatagrams.WriteAck(datagram, tagRemote, keyPos: 0x40, seqNum: 99, advertisedWindow: 0x19000);

        var wire = new RecordingTakionWire();

        Assert.Equal(
            ChiakiError.Success,
            TakionSendPath.Send(datagram, null, wire, new object()));

        byte[] onTheWire = Assert.Single(wire.Sent);

        using var keyState = new KeyState();
        TakionMessageReading read = TakionMessageIntake.Read(onTheWire, tagRemote, keyState);

        Assert.Equal(TakionMessageVerdict.DataAck, read.Verdict);
        Assert.Equal(TakionDataDatagrams.AckBodySize, read.PayloadSize);
    }
}

