using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP676: the three sends outside PP497's MAC table, each with its own offsets.
///
/// The feedback state, the feedback history and the microphone packet encrypt their own payload,
/// write their own key position and compute their own GMAC before calling the raw send. A port that
/// reached for the table would put the MAC where nothing reads it.
///
/// WHAT THESE HOLD IS THE ORDER AND THE TWO POSITIONS, because both read backwards and neither
/// fails loudly when wrong:
///
///   the payload is encrypted at the position PLUS ONE BLOCK, while the GMAC is taken at the
///   position itself - so a port using one number for both desynchronises a stream cipher, which
///   sounds like noise rather than reporting anything;
///
///   the position is written BEFORE the MAC is computed, so the MAC covers it. Stamping first
///   produces a MAC over a zero field, and a console rejects every feedback packet in silence.
/// </summary>
public class TakionFeedbackSendsTests(ITestOutputHelper output)
{
    /// <summary>A cipher that records what it was asked, and xors a recognisable pattern.</summary>
    private sealed class Recording
    {
        public List<ulong> EncryptedAt { get; } = [];
        public List<ulong> MacAt { get; } = [];
        public List<byte[]> MacOver { get; } = [];

        public void Encrypt(ulong at, Span<byte> payload)
        {
            EncryptedAt.Add(at);

            for (int i = 0; i < payload.Length; i++)
                payload[i] ^= (byte)(at + (ulong)i);
        }

        public byte[] Gmac(ulong at, ReadOnlyMemory<byte> packet)
        {
            MacAt.Add(at);
            MacOver.Add(packet.ToArray());
            return [0x11, 0x22, 0x33, 0x44];
        }
    }

    private static byte[] FeedbackState(ushort seqNum, int payloadSize)
    {
        var packet = new byte[TakionFeedbackSends.Feedback.HeadSize + payloadSize];

        TakionFeedbackSends.WriteFeedbackHead(packet, TakionFeedbackSends.FeedbackStateType, seqNum);

        for (int i = TakionFeedbackSends.Feedback.HeadSize; i < packet.Length; i++)
            packet[i] = (byte)(i + 0x60);

        return packet;
    }

    /// <summary>The head: type, sequence, a zero byte, and two zeroed fields the send fills.</summary>
    [Theory]
    [InlineData(TakionFeedbackSends.FeedbackStateType, (ushort)0)]
    [InlineData(TakionFeedbackSends.FeedbackStateType, (ushort)0xFFFF)]
    [InlineData(TakionFeedbackSends.FeedbackHistoryType, (ushort)0x1234)]
    public void TheFeedbackHeadIsTheCsLayout(byte type, ushort seqNum)
    {
        var packet = new byte[TakionFeedbackSends.Feedback.HeadSize + 8];
        packet.AsSpan().Fill(0xEE);

        TakionFeedbackSends.WriteFeedbackHead(packet, type, seqNum);

        Assert.Equal(type, packet[0]);
        Assert.Equal(seqNum, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(1)));
        Assert.Equal(0, packet[3]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8)));

        // Past the head, untouched - the head writer owns twelve bytes and no more.
        Assert.Equal(0xEE, packet[TakionFeedbackSends.Feedback.HeadSize]);
    }

    /// <summary>
    /// THE TWO POSITIONS: the payload encrypts one block ABOVE where the MAC is taken.
    ///
    /// The single most consequential number in this file, and the one a port collapses into one.
    /// </summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(16UL)]
    [InlineData(0x1234UL)]
    [InlineData(0xFFFFFFFFUL)]
    public void ThePayloadEncryptsOneBlockAboveTheMac(ulong keyPos)
    {
        var cipher = new Recording();
        var wire = new RecordingTakionWire();

        TakionFeedbackSends.Send(
            FeedbackState(1, 16), TakionFeedbackSends.Feedback, keyPos,
            cipher.Encrypt, cipher.Gmac, wire, new object());

        Assert.Equal(keyPos + TakionFeedbackSends.BlockSize, Assert.Single(cipher.EncryptedAt));
        Assert.Equal(keyPos, Assert.Single(cipher.MacAt));
    }

    /// <summary>
    /// THE ORDER: the MAC is taken over a packet that ALREADY carries the position.
    ///
    /// Asserted by reading what the cipher was handed rather than by watching calls, because the
    /// consequence is about the bytes: a MAC over a zeroed field is what a port that stamped in the
    /// other order produces, and the console's refusal is silent.
    /// </summary>
    [Fact]
    public void TheMacCoversThePositionThatWasJustWritten()
    {
        var cipher = new Recording();
        var wire = new RecordingTakionWire();
        const ulong keyPos = 0xABCDEF01;

        TakionFeedbackSends.Send(
            FeedbackState(7, 24), TakionFeedbackSends.Feedback, keyPos,
            cipher.Encrypt, cipher.Gmac, wire, new object());

        byte[] macOver = Assert.Single(cipher.MacOver);

        Assert.Equal(
            (uint)keyPos,
            BinaryPrimitives.ReadUInt32BigEndian(macOver.AsSpan(TakionFeedbackSends.Feedback.KeyPosOffset)));

        // And the MAC field itself was still zero when the MAC was taken, which is the other half.
        Assert.Equal(
            0u,
            BinaryPrimitives.ReadUInt32BigEndian(macOver.AsSpan(TakionFeedbackSends.Feedback.MacOffset)));
    }

    /// <summary>And what reaches the wire carries the MAC, at this layout's offset.</summary>
    [Fact]
    public void TheSentPacketCarriesTheMacAtTheLayoutsOffset()
    {
        var cipher = new Recording();
        var wire = new RecordingTakionWire();

        TakionFeedbackSends.Send(
            FeedbackState(3, 16), TakionFeedbackSends.Feedback, 0x40,
            cipher.Encrypt, cipher.Gmac, wire, new object());

        byte[] sent = Assert.Single(wire.Sent);

        Assert.Equal(
            new byte[] { 0x11, 0x22, 0x33, 0x44 },
            sent.AsSpan(TakionFeedbackSends.Feedback.MacOffset, 4).ToArray());

        Assert.Equal(0x40u, BinaryPrimitives.ReadUInt32BigEndian(sent.AsSpan(4)));
    }

    /// <summary>
    /// THE MICROPHONE'S OFFSETS ARE DIFFERENT, and its head grows by a byte on a PS5.
    ///
    /// Nineteen or twenty bytes of head, the position at fourteen and the MAC at ten - none of
    /// which the feedback layout shares. The generation byte shifts what gets encrypted, which is
    /// the whole reason it is a layout rather than a flag read inside the send.
    /// </summary>
    [Theory]
    [InlineData(false, 19)]
    [InlineData(true, 20)]
    public void TheMicrophoneHasItsOwnOffsetsAndTwoHeadSizes(bool ps5, int headSize)
    {
        TakionCryptLayout layout = TakionFeedbackSends.MicrophoneFor(ps5);

        Assert.Equal(headSize, layout.HeadSize);
        Assert.Equal(14, layout.KeyPosOffset);
        Assert.Equal(10, layout.MacOffset);

        var cipher = new Recording();
        var wire = new RecordingTakionWire();
        var packet = new byte[headSize + 32];

        TakionFeedbackSends.Send(packet, layout, 0x100, cipher.Encrypt, cipher.Gmac, wire, new object());

        byte[] sent = Assert.Single(wire.Sent);

        Assert.Equal(0x100u, BinaryPrimitives.ReadUInt32BigEndian(sent.AsSpan(14)));
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, sent.AsSpan(10, 4).ToArray());

        // The PS5 byte shifts the payload, so the encrypted region is one shorter for the same packet.
        Assert.Equal(32, packet.Length - headSize);
    }

    /// <summary>Only the payload is encrypted; the head goes out in the clear.</summary>
    [Fact]
    public void TheHeadIsNotEncrypted()
    {
        var cipher = new Recording();
        var wire = new RecordingTakionWire();

        byte[] packet = FeedbackState(9, 16);
        byte[] headBefore = packet.AsSpan(0, TakionFeedbackSends.Feedback.HeadSize).ToArray();

        TakionFeedbackSends.Send(
            packet, TakionFeedbackSends.Feedback, 0, cipher.Encrypt, cipher.Gmac, wire, new object());

        // Everything up to the position field is as it was; the position and MAC are the send's.
        Assert.Equal(headBefore[..4], packet[..4].ToArray());
    }

    /// <summary>
    /// The ledger advances by the payload PLUS A BLOCK, which is the gap between the two positions.
    ///
    /// Stated as its own function because getting it wrong desynchronises the cipher for the rest
    /// of the session rather than failing at the packet that did it.
    /// </summary>
    [Theory]
    [InlineData(0, 16)]
    [InlineData(1, 17)]
    [InlineData(0x19, 0x29)]
    [InlineData(0x1c, 0x2c)]
    public void TheLedgerAdvancesByThePayloadAndABlock(int payloadSize, int expected)
        => Assert.Equal(expected, TakionFeedbackSends.LedgerAdvanceFor(payloadSize));

    /// <summary>
    /// The lock is held across the whole sequence and is reentrant for its holder.
    ///
    /// The C makes gkcrypt_local_mutex recursive precisely because these callers hold it while the
    /// position advance takes it too. A private lock would deadlock on the first feedback packet.
    /// </summary>
    [Fact]
    public void ACallerAlreadyHoldingTheLockIsNotDeadlocked()
    {
        var cipher = new Recording();
        var wire = new RecordingTakionWire();
        var cipherLock = new object();

        lock (cipherLock)
        {
            Assert.Equal(
                ChiakiError.Success,
                TakionFeedbackSends.Send(
                    FeedbackState(1, 8), TakionFeedbackSends.Feedback, 0,
                    cipher.Encrypt, cipher.Gmac, wire, cipherLock));
        }

        Assert.Single(wire.Sent);
    }

    /// <summary>And the wire is called with it still held, which the send happening inside means.</summary>
    [Fact]
    public void TheWireIsCalledWithTheLockStillHeld()
    {
        var cipher = new Recording();
        var cipherLock = new object();
        bool takenByAnother = false;

        var wire = new RecordingTakionWire
        {
            OnSend = _ =>
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
            },
        };

        TakionFeedbackSends.Send(
            FeedbackState(1, 8), TakionFeedbackSends.Feedback, 0,
            cipher.Encrypt, cipher.Gmac, wire, cipherLock);

        Assert.False(takenByAnother, "another thread took the cipher lock mid-send");
    }

    /// <summary>The two feedback state sizes are the C's, and the head is on top of them.</summary>
    [Fact]
    public void TheFeedbackStateSizesAreTheCs()
    {
        Assert.Equal(0x19, TakionFeedbackSends.FeedbackStateV9);
        Assert.Equal(0x1c, TakionFeedbackSends.FeedbackStateV12);
        Assert.Equal(0xc, TakionFeedbackSends.Feedback.HeadSize);

        output.WriteLine(
            $"v9 packet {0xc + TakionFeedbackSends.FeedbackStateV9}, "
                + $"v12 {0xc + TakionFeedbackSends.FeedbackStateV12}");
    }

    /// <summary>A packet shorter than its own head is refused rather than written past.</summary>
    [Fact]
    public void APacketShorterThanItsHeadIsRefused()
    {
        var cipher = new Recording();
        var wire = new RecordingTakionWire();

        Assert.Equal(
            ChiakiError.BufTooSmall,
            TakionFeedbackSends.Send(
                new byte[4], TakionFeedbackSends.Feedback, 0,
                cipher.Encrypt, cipher.Gmac, wire, new object()));

        Assert.Empty(wire.Sent);
    }

    /// <summary>And the head writer refuses a span too small for it.</summary>
    [Fact]
    public void AShortHeadSpanIsRefused()
        => Assert.Throws<ArgumentException>(
            () => TakionFeedbackSends.WriteFeedbackHead(new byte[4], TakionFeedbackSends.FeedbackStateType, 1));
}
