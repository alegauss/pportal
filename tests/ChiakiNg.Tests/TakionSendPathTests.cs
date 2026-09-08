using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP675, PP811: chiaki_takion_send - the MAC under one lock, and the socket outside it.
///
/// THE ASSERTION BELOW IS INVERTED FROM THE ONE PP675 WROTE, and the inversion is the finding.
/// PP675 argued the C must hold gkcrypt_local_mutex across the stamp AND the send: two threads that
/// stamped under a lock and then raced to the socket would put their datagrams on the wire in an
/// order neither key position matches, and a stream cipher fed positions out of order produces
/// noise rather than an error. The argument is reasonable and takion.c does not make it -
/// chiaki_takion_send unlocks between chiaki_takion_packet_mac and chiaki_takion_send_raw, three
/// lines apart. The mutex guards the CIPHER, which is the only shared thing the MAC touches.
///
/// SO THE ORDERING IS STILL WHAT THESE HOLD, only the other way round, and it is still observable
/// to nothing but something watching both ends. RecordingTakionWire runs a hook at the moment of
/// the send, so a test can ask whether the lock was held when the datagram left - and the answer
/// this asserts is no.
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
    /// PP811: THE SEND IS OUTSIDE THE LOCK, asked of the lock itself at the moment of the send.
    ///
    /// A second thread tries to take the same lock while the wire's hook is running, and it MUST
    /// get it. That is the C's shape: the cipher is released before send_raw, so a second sender
    /// can be stamping while the first is still in the socket call. Asserting the opposite is what
    /// this test did until the C was read, and nothing else in the tree disagreed with it.
    /// </summary>
    [Fact]
    public void TheWireIsCalledWithTheLockAlreadyReleased()
    {
        var cipherLock = new object();
        var wire = new RecordingTakionWire();
        bool takenByAnother = false;

        wire.OnSend = _ =>
        {
            var other = new Thread(() =>
            {
                if (Monitor.TryEnter(cipherLock, TimeSpan.FromSeconds(2)))
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
        Assert.True(takenByAnother, "the cipher lock was still held while the datagram was being sent");
    }

    /// <summary>
    /// PP811: AND THE STAMP ITSELF IS UNDER IT, which is the half the lock is actually for.
    ///
    /// Without this the test above would pass a path that took no lock at all - and a path that
    /// took none would be wrong in the way the C's recursive mutex exists to prevent, rather than
    /// right in the way the C is. Asked of the gmac function, which only runs inside.
    /// </summary>
    [Fact]
    public void TheGmacRunsWithTheLockHeld()
    {
        var cipherLock = new object();
        var wire = new RecordingTakionWire();
        bool takenDuringTheStamp = false;

        TakionSendPath.Send(
            Datagram(),
            _ =>
            {
                var other = new Thread(() =>
                {
                    if (Monitor.TryEnter(cipherLock, TimeSpan.FromMilliseconds(200)))
                    {
                        takenDuringTheStamp = true;
                        Monitor.Exit(cipherLock);
                    }
                });

                other.Start();
                other.Join(TimeSpan.FromSeconds(5));

                return [0xDE, 0xAD, 0xBE, 0xEF];
            },
            wire,
            cipherLock);

        Assert.Single(wire.Sent);
        Assert.False(takenDuringTheStamp, "another thread took the cipher lock while the MAC was being stamped");
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

    /// <summary>
    /// PP811: THE LOCK'S EXTENT, READ OUT OF takion.c RATHER THAN ARGUED FOR.
    ///
    /// This is the assertion the file was missing for five blocks. PP675 reasoned its way to a
    /// wider critical section than the C has and wrote the reasoning into the prose, where nothing
    /// could contradict it - the module had no caller, so no behaviour disagreed either. Three
    /// positions in one body answer it instead: the lock, the unlock, and the raw send.
    /// </summary>
    [Fact]
    public void TheCipherIsReleasedBeforeTheRawSendInTheC()
    {
        if (TakionSendPathSource.Locate() is not { } path)
            return;

        string? body = TakionSendPathSource.SendBody(File.ReadAllText(path));
        Assert.NotNull(body);

        Assert.True(
            TakionSendPathSource.TheCipherIsReleasedBeforeTheRawSend(body),
            "chiaki_takion_send no longer unlocks gkcrypt_local before send_raw");

        Assert.True(
            TakionSendPathSource.TheMacIsTakenUnderTheCipher(body),
            "chiaki_takion_send no longer takes the MAC while gkcrypt_local is held");
    }

    /// <summary>
    /// AND THE FEEDBACK PATH STILL DOES HOLD IT ACROSS ITS SEND, which is the contrast.
    ///
    /// The two together are the finding: one send releases the cipher before the socket and one
    /// does not, in the same file. A port that read either rule for both would be wrong about one
    /// of them - and reading the feedback one for both is exactly the mistake PP675 made.
    /// </summary>
    [Fact]
    public void TheFeedbackPathKeepsTheWiderSection()
    {
        if (TakionSendPathSource.Locate() is not { } path)
            return;

        Assert.True(
            TakionSendPathSource.TheFeedbackPathStillHoldsItAcrossTheSend(File.ReadAllText(path)),
            "takion_send_feedback_packet no longer holds gkcrypt_local across send_raw");
    }

    /// <summary>
    /// A body that never locked would pass the release check, so the two are asked of one text.
    ///
    /// TheCipherIsReleasedBeforeTheRawSend is an ORDER over three positions, and an order over
    /// positions that do not exist is vacuously false rather than true - which is worth holding,
    /// because the failure it would otherwise hide is the C dropping the lock entirely.
    /// </summary>
    [Fact]
    public void ABodyWithNoLockSatisfiesNeitherCheck()
    {
        const string unlocked = "{ chiaki_takion_packet_mac(a, b, c, d, e, f); chiaki_takion_send_raw(a, b, c); }";

        Assert.False(TakionSendPathSource.TheCipherIsReleasedBeforeTheRawSend(unlocked));
        Assert.False(TakionSendPathSource.TheMacIsTakenUnderTheCipher(unlocked));
    }
}

