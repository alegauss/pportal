using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP706, under PP52: the microphone's four pieces run as one path.
///
/// PP652 opened the capture, PP676 transcribed the head, PP694 wrote the encoder, and none of them
/// had met another. audiosender.c was the only thing that composed them and it is 143 lines.
///
/// THE WHOLE PATH IS ASSERTED HERE, from a captured buffer to a packet: bytes into
/// <see cref="MicrophoneUnits"/>, whole units into <see cref="ManagedOpusEncoder"/>, forty-byte
/// frames into <see cref="ManagedAudioSender"/>, and packets out with PP676's eleven fields at
/// PP676's offsets. That is the assertion PP706 owed and the one no piece could make alone.
///
/// AND THE C'S TWO SURPRISES ARE HELD RATHER THAN TIDIED. The first two frames of a session send
/// nothing, and every packet carries its newest frame twice and its oldest never - because the C
/// copies the arrival over slot zero after filling all three. Both are reproduced and both have a
/// test that would fail if somebody improved them.
/// </summary>
public class ManagedAudioSenderTests(ITestOutputHelper output)
{
    private const int Rate = 48000;
    private const int Channels = 1;
    private const int FrameSize = 480;

    /// <summary>A frame of a size, filled distinguishably so a slot can be told from another.</summary>
    private static byte[] Frame(byte mark)
    {
        var frame = new byte[ManagedAudioSender.UnitBytes];
        Array.Fill(frame, mark);
        return frame;
    }

    /// <summary>The three units a packet carries, as the marks that were fed.</summary>
    private static byte[] Slots(byte[] packet, int headBytes)
        =>
        [
            packet[headBytes],
            packet[headBytes + ManagedAudioSender.UnitBytes],
            packet[headBytes + (2 * ManagedAudioSender.UnitBytes)],
        ];

    /// <summary>
    /// TWO FRAMES OF SILENCE, then a packet on every one after.
    ///
    /// The C keeps the first arrival, keeps the second, and sends on the third. A path that sent on
    /// the first would put uninitialised slots on the wire, and one that never sent would be the
    /// state PP706 was filed about.
    /// </summary>
    [Fact]
    public void TheFirstTwoFramesSendNothingAndTheThirdSends()
    {
        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: true, one => packets.Add(one.ToArray()));

        Assert.Equal(MicSendOutcome.FilledTheSecondSlot, sender.OpusData(Frame(1)));
        Assert.Equal(MicSendOutcome.FilledTheFirstSlot, sender.OpusData(Frame(2)));
        Assert.Empty(packets);

        Assert.Equal(MicSendOutcome.Sent, sender.OpusData(Frame(3)));
        Assert.Equal(MicSendOutcome.Sent, sender.OpusData(Frame(4)));

        Assert.Equal(2, packets.Count);
        Assert.All(packets, one => Assert.Equal(sender.PacketBytes, one.Length));
    }

    /// <summary>
    /// THE FINDING, reproduced: the newest frame goes twice and the oldest never.
    ///
    /// The C fills slot zero with the older kept frame, slot one with the newer, slot two with the
    /// arrival - and then copies the arrival over slot zero. So a packet carrying three units of
    /// redundancy carries two distinct frames, and a listener losing the middle one has lost a frame
    /// that was sent once.
    ///
    /// Marked frames rather than a byte comparison, because what is being asserted is WHICH frame is
    /// in each slot and a comparison against expected bytes would say the same thing less clearly.
    /// </summary>
    [Fact]
    public void EveryPacketCarriesTheNewestFrameTwiceAndTheOldestNever()
    {
        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: false, one => packets.Add(one.ToArray()));

        sender.OpusData(Frame(1));
        sender.OpusData(Frame(2));
        sender.OpusData(Frame(3));

        byte[] slots = Slots(packets[0], sender.HeadBytes);
        output.WriteLine($"first packet: {slots[0]}, {slots[1]}, {slots[2]}");

        // Frame 1 is the oldest and is nowhere; frame 3 is in two slots.
        Assert.Equal([(byte)3, (byte)2, (byte)3], slots);
        Assert.DoesNotContain((byte)1, slots);

        sender.OpusData(Frame(4));
        byte[] next = Slots(packets[1], sender.HeadBytes);
        output.WriteLine($"second packet: {next[0]}, {next[1]}, {next[2]}");

        Assert.Equal([(byte)4, (byte)3, (byte)4], next);
    }

    /// <summary>
    /// The head is PP676's, at PP676's offsets, with the C's own values.
    ///
    /// Read back field by field from the bytes rather than compared against a blob: what this is
    /// checking is that the composition hands the writer the right things, and a blob comparison
    /// would pass on two wrong values that cancelled.
    /// </summary>
    [Fact]
    public void TheHeadCarriesTheCsFieldsAtTheCsOffsets()
    {
        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: true, one => packets.Add(one.ToArray()));

        for (var i = 0; i < 3; i++)
            sender.OpusData(Frame((byte)(i + 1)));

        byte[] packet = Assert.Single(packets);

        Assert.Equal(ManagedAudioSender.PacketType, packet[0]);

        // BIG-endian on the wire: the C byte-swaps each field and then stores it raw.
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(1)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(3)));
        Assert.Equal(ManagedAudioSender.UnitsNumber, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(5)));
        Assert.Equal(ManagedAudioSender.Codec, packet[9]);

        // Ten and fourteen stay zero: the send writes the MAC and the key position.
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(10)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(14)));

        Assert.Equal(0, packet[18]);
        Assert.Equal(0, packet[19]);

        // The packed word's three fields, which is the audio layout of the AV header's own.
        uint packed = ManagedAudioSender.UnitsNumber;
        Assert.Equal(ManagedAudioSender.UnitsInFrameFecRaw, packed & 0xffff);
        Assert.Equal(ManagedAudioSender.UnitsInFrameTotal - 1, (packed >> 0x10) & 0xff);
        Assert.Equal(0u, (packed >> 0x18) & 0xff);
    }

    /// <summary>
    /// The counter moves after the send, and the two head fields are one apart.
    ///
    /// packet_index is frame_index and the frame_index field is frame_index PLUS ONE, which reads
    /// like a mistake and is what the C does. Moving the counter before the send would put the next
    /// packet's number on this one.
    /// </summary>
    [Fact]
    public void TheCounterMovesAfterTheSendAndTheTwoFieldsAreOneApart()
    {
        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: false, one => packets.Add(one.ToArray()));

        for (var i = 0; i < 5; i++)
            sender.OpusData(Frame((byte)(i + 1)));

        Assert.Equal(3, packets.Count);
        Assert.Equal(3, sender.Sent);
        Assert.Equal(3, sender.FrameIndex);

        for (var i = 0; i < packets.Count; i++)
        {
            ushort packetIndex = BinaryPrimitives.ReadUInt16BigEndian(packets[i].AsSpan(1));
            ushort frameIndex = BinaryPrimitives.ReadUInt16BigEndian(packets[i].AsSpan(3));

            Assert.Equal(i, packetIndex);
            Assert.Equal(packetIndex + 1, frameIndex);
        }
    }

    /// <summary>
    /// A frame that is not the unit size is dropped here as well as by the encoder.
    ///
    /// PP694 measured that silence encodes to three bytes and opusencoder.c drops it as a protocol
    /// violation; this is the same test at the other end of the path, which the C also has. A path
    /// that only had one of them would send a short unit into a fixed-size buffer.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(39)]
    [InlineData(41)]
    public void AFrameThatIsNotTheUnitSizeIsDropped(int bytes)
    {
        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: false, one => packets.Add(one.ToArray()));

        Assert.Equal(MicSendOutcome.WrongSize, sender.OpusData(new byte[bytes]));
        Assert.Equal(1, sender.Dropped);
        Assert.Empty(packets);
    }

    /// <summary>
    /// A PS5's head is one byte longer, and the payload starts one byte later.
    ///
    /// The whole of the generation difference, and the reason it is worth a test: both packets are
    /// otherwise identical, so a port that got the size right and the offset wrong would produce a
    /// packet of the right length with its audio shifted.
    /// </summary>
    [Fact]
    public void APs5HeadIsOneByteLongerAndThePayloadFollowsIt()
    {
        var four = new List<byte[]>();
        var five = new List<byte[]>();

        var ps4 = new ManagedAudioSender(ps5: false, one => four.Add(one.ToArray()));
        var ps5 = new ManagedAudioSender(ps5: true, one => five.Add(one.ToArray()));

        for (var i = 0; i < 3; i++)
        {
            ps4.OpusData(Frame(7));
            ps5.OpusData(Frame(7));
        }

        Assert.Equal(MicPacketHead.Size, ps4.HeadBytes);
        Assert.Equal(MicPacketHead.SizePs5, ps5.HeadBytes);
        Assert.Equal(ps4.PacketBytes + 1, ps5.PacketBytes);

        Assert.Equal(7, four[0][MicPacketHead.Size]);
        Assert.Equal(7, five[0][MicPacketHead.SizePs5]);
    }

    /// <summary>
    /// THE WHOLE PATH: captured bytes in at one end, packets out at the other.
    ///
    /// The assertion PP706 owed. A buffer of PCM the shape WasapiCapture delivers goes into the unit
    /// splitter, whole units into the encoder, encoded frames into the sender - and what comes out
    /// is packets of the right length whose head says what it should.
    ///
    /// The drop count is the join that makes it one path rather than three: every frame the encoder
    /// refused is a frame the sender never saw, and the two counts have to add up to what went in.
    /// </summary>
    [Fact]
    public void CapturedBytesBecomePacketsThroughTheWholePath()
    {
        var packets = new List<byte[]>();
        var sender = new ManagedAudioSender(ps5: true, one => packets.Add(one.ToArray()));

        using var encoder = new ManagedOpusEncoder();
        Assert.True(encoder.Header(Rate, Channels));

        var units = new MicrophoneUnits();
        var encoded = 0;
        var refused = 0;

        // Half a second of a tone, handed over in the ragged chunks a capture actually delivers.
        byte[] captured = Pcm(TimeSpan.FromMilliseconds(500));
        var at = 0;

        while (at < captured.Length)
        {
            int take = Math.Min(700, captured.Length - at);

            units.Take(captured.AsSpan(at, take), unit =>
            {
                OpusFrameOutcome outcome = encoder.Frame(
                    System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(unit),
                    out ReadOnlySpan<byte> frame);

                if (outcome == OpusFrameOutcome.Sent)
                {
                    encoded++;
                    sender.OpusData(frame);
                }
                else
                {
                    refused++;
                }
            });

            at += take;
        }

        output.WriteLine(
            $"{units.Emitted} unit(s), {encoded} encoded, {refused} refused, {packets.Count} packet(s)");

        Assert.True(units.Emitted > 0, "the splitter emitted nothing, so nothing reached the encoder");
        Assert.Equal(units.Emitted, encoded + refused);

        // Two frames are kept before the first packet, which is the C's warm-up.
        Assert.Equal(Math.Max(encoded - 2, 0), packets.Count);
        Assert.All(packets, one => Assert.Equal(sender.PacketBytes, one.Length));
        Assert.All(packets, one => Assert.Equal(ManagedAudioSender.PacketType, one[0]));
    }

    /// <summary>Deterministic PCM in the announced format, which is what the capture delivers.</summary>
    private static byte[] Pcm(TimeSpan length)
    {
        int samples = (int)(Rate * length.TotalSeconds);
        var bytes = new byte[samples * 2];
        var rng = new Random(20260904);

        for (var i = 0; i < samples; i++)
        {
            double t = i / (double)Rate;
            double v = (0.42 * Math.Sin(2 * Math.PI * 440 * t)) + (0.03 * (rng.NextDouble() - 0.5));

            BinaryPrimitives.WriteInt16LittleEndian(
                bytes.AsSpan(i * 2), (short)Math.Clamp(v * 32000, short.MinValue, short.MaxValue));
        }

        return bytes;
    }

    /// <summary>The C's own composition, so the port cannot drift off the file it copies.</summary>
    [Fact]
    public void TheCsOwnOrderStillHolds()
    {
        if (ManagedAudioSenderSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        string body = Assert.IsType<string>(ManagedAudioSenderSource.Body(source));

        Assert.True(ManagedAudioSenderSource.AWrongSizedFrameIsDroppedFirst(body));
        Assert.True(ManagedAudioSenderSource.TheFirstTwoArrivalsAreKept(body));
        Assert.True(
            ManagedAudioSenderSource.TheArrivalOverwritesTheOldestSlot(body),
            "the C stopped overwriting slot zero, so this port is now sending a packet it does not");

        Assert.True(ManagedAudioSenderSource.TheCounterMovesAfterTheSend(source));

        Assert.Equal(
            (ManagedAudioSender.UnitBytes, ManagedAudioSender.UnitsPerPacket),
            ManagedAudioSenderSource.SizesIn(source));
    }

    /// <summary>And each reader refuses a body that lost what it names.</summary>
    [Fact]
    public void EachSourceReaderRefusesABodyThatLostIt()
    {
        Assert.False(ManagedAudioSenderSource.AWrongSizedFrameIsDroppedFirst("return;"));
        Assert.False(ManagedAudioSenderSource.TheFirstTwoArrivalsAreKept("if(!audio_sender->frameb)"));
        Assert.False(ManagedAudioSenderSource.TheArrivalOverwritesTheOldestSlot(
            "memcpy(audio_sender->frame_buf, audio_sender->frameb, audio_sender->buf_size_per_unit);"));
        Assert.False(ManagedAudioSenderSource.TheCounterMovesAfterTheSend("nothing"));
        Assert.Null(ManagedAudioSenderSource.SizesIn("nothing"));
    }
}
