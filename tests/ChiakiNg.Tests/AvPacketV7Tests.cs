using System.Buffers.Binary;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP679, under PP27: version seven's AV header - the parse and takion.c's only formatter.
///
/// TWO ORACLES, one per direction. The parse is held to chiaki_takion_v7_av_packet_parse over
/// synthetic headers and over the 4025 real ones PP608 recorded; the formatter is held to
/// chiaki_takion_v7_av_packet_format_header byte for byte, and then its output is handed BACK to
/// the C's parse - which is the check that would catch a formatter and a parse agreeing with each
/// other about a layout neither shares with the console.
///
/// THE FOUR DIFFERENCES FROM V9 EACH HAVE THEIR OWN CHECK. Three were visible from the C:
/// the bound counting the nalu-info add for video too, the packed word always taking the video
/// layout, and the key position being thirty-two raw bits. The fourth came out of writing the two
/// walks side by side - v7 has no audio arm, so the same audio datagram's payload starts a byte
/// earlier here - and it is the one a differential over v9-shaped input would have found last.
/// </summary>
public class AvPacketV7Tests(ITestOutputHelper output)
{
    private const byte AudioPlain = 0x03;
    private const byte AudioWithNaluFlag = 0x13;
    private const byte VideoPlain = 0x02;
    private const byte VideoWithNaluFlag = 0x12;

    /// <summary>A packet of a size, led by a type byte, with distinguishable bytes after it.</summary>
    private static byte[] Packet(byte firstByte, int size)
    {
        var packet = new byte[size];
        packet[0] = firstByte;

        for (var i = 1; i < size; i++)
            packet[i] = (byte)(i + 0x10);

        return packet;
    }

    /// <summary>The wrappers are in this build, so the differentials below are comparisons.</summary>
    [Fact]
    public void TheShimCarriesBothWrappers() => Assert.True(NativeAvPacketV7.IsAvailable());

    /// <summary>
    /// THE PARSE DIFFERENTIAL, over every length either side of the bound and all four lead bytes.
    ///
    /// No key state on either side, which is the third difference: the C's parameter for one is
    /// declared and never read, so there is no ledger to keep in step and no reason to hand each
    /// side its own.
    /// </summary>
    [Theory]
    [InlineData(AudioPlain)]
    [InlineData(AudioWithNaluFlag)]
    [InlineData(VideoPlain)]
    [InlineData(VideoWithNaluFlag)]
    public void TheManagedParseAgreesWithTheC(byte lead)
    {
        int agreed = 0, refusedBoth = 0;

        for (var size = 1; size <= 64; size++)
        {
            byte[] bytes = Packet(lead, size);

            V7AvHeader? theirs = NativeAvPacketV7.Parse((byte[])bytes.Clone(), out ChiakiError theirError);
            V7AvHeader? ours = AvPacketV7.Parse(bytes, out ChiakiError ourError);

            Assert.Equal(theirError, ourError);

            if (theirs is null)
            {
                Assert.Null(ours);
                refusedBoth++;
                continue;
            }

            Assert.NotNull(ours);
            Assert.Equal(theirs.Value, ours.Value);
            agreed++;
        }

        output.WriteLine($"lead 0x{lead:x2}: {agreed} agreed, {refusedBoth} refused by both");

        // PP271: a comparison against nothing matches. Both outcomes have to occur, or this passed
        // by refusing everything or by never reaching the bound.
        Assert.True(agreed > 0, "no length parsed, so the agreement above is about refusals only");
        Assert.True(refusedBoth > 0, "no length was refused, so the bound was never exercised");
    }

    /// <summary>
    /// AND OVER REAL HEADS: PP608's 4025 datagrams, whichever version sent them.
    ///
    /// They are v9 traffic and that is the point - the two parsers read the same bytes differently,
    /// so a managed v7 body that had quietly become v9's would agree with the shim's v9 export and
    /// disagree with this one. Eighteen bytes is exactly a v7 audio header, so the audio rows parse
    /// with an empty payload, the video rows are refused for want of three more bytes, and the
    /// control rows are refused by kind. All three outcomes are asserted to occur.
    /// </summary>
    [Fact]
    public void TheManagedParseAgreesWithTheCOverTheRecordedHeads()
    {
        if (DatagramCorpus.Read() is not { } corpus)
            return;

        int parsed = 0, tooSmall = 0, wrongKind = 0;

        foreach (CapturedDatagram datagram in corpus)
        {
            // Twice: as recorded, and padded out so the video rows reach the parse too. Eighteen
            // bytes is exactly a v7 audio header, so without the second pass the video arm of this
            // comparison would only ever see a refusal.
            foreach (byte[] head in new[] { datagram.Head, Padded(datagram.Head, 32) })
            {
                V7AvHeader? theirs = NativeAvPacketV7.Parse((byte[])head.Clone(), out ChiakiError theirError);
                V7AvHeader? ours = AvPacketV7.Parse(head, out ChiakiError ourError);

                Assert.Equal(theirError, ourError);
                Assert.Equal(theirs, ours);

                switch (ourError)
                {
                    case ChiakiError.Success: parsed++; break;
                    case ChiakiError.BufTooSmall: tooSmall++; break;
                    default: wrongKind++; break;
                }
            }
        }

        output.WriteLine($"{corpus.Count} heads: {parsed} parsed, {tooSmall} too small, {wrongKind} not AV");

        Assert.Equal(DatagramCorpus.Datagrams, corpus.Count);
        Assert.True(parsed > 0, "no recorded head parsed, so the agreement is about refusals only");
        Assert.True(tooSmall > 0, "no recorded head was refused for its length");
        Assert.True(wrongKind > 0, "no recorded head was refused by kind");
    }

    /// <summary>A head with a fixed tail, so a length the capture truncated reaches the walk.</summary>
    private static byte[] Padded(byte[] head, int size)
    {
        byte[] padded = new byte[Math.Max(size, head.Length)];
        head.CopyTo(padded, 0);

        for (int i = head.Length; i < padded.Length; i++)
            padded[i] = (byte)(i + 0x10);

        return padded;
    }

    /// <summary>
    /// Every combination of fields the formatter reads, edges included.
    ///
    /// A plain list rather than TheoryData: a record struct is not a serializable theory argument,
    /// and this project turns warnings into errors precisely so a test cannot say less than it
    /// looks like it says.
    /// </summary>
    private static IEnumerable<V7AvHeader> Headers()
    {
        foreach (bool isVideo in new[] { false, true })
        {
            foreach (bool nalu in new[] { false, true })
            {
                // All zero, which is what senkusha's memset leaves behind.
                yield return new V7AvHeader(isVideo, nalu, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                // Senkusha's own: codec 0xff, a total of 0x800, an index that walks.
                yield return new V7AvHeader(isVideo, nalu, 0, 3, 7, 0x800, 0, 0xff, 0, 0, 0, 0, 0);

                // Ordinary values in every field at once.
                yield return new V7AvHeader(
                    isVideo, nalu, 0x1234, 0x5678, 0x2ab, 0x101, 0x0cd, 5, 0x9abc, 3, 0xdeadbeef, 0, 0);

                // Every field at its width, where the packing has to truncate rather than carry.
                yield return new V7AvHeader(
                    isVideo, nalu, 0xffff, 0xffff, 0xffff, 0xffff, 0xffff, 0xff, 0xffff, 0xff,
                    0xffffffff, 0, 0);

                // A total of zero, whose minus one wraps before it is masked.
                yield return new V7AvHeader(isVideo, nalu, 1, 2, 0x7ff, 0, 0x3ff, 1, 0xffff, 7, 1, 0, 0);
            }
        }
    }

    /// <summary>
    /// THE FORMATTER DIFFERENTIAL: the same bytes, and the same bytes left alone.
    ///
    /// Both buffers are pre-filled with a pattern and compared whole, so "writes the header" and
    /// "writes nothing past it" are one assertion. The second half is load-bearing: senkusha puts
    /// its tag after the header and would lose it to a formatter that cleared behind itself.
    ///
    /// The widest cases are the ones worth having. A unit index of 0xffff has its top bits shifted
    /// off the end of a 32-bit word, and a total of zero has its minus one wrap before the mask -
    /// both are arithmetic a managed rewrite could widen without noticing.
    /// </summary>
    [Fact]
    public void TheManagedFormatterWritesTheCsBytes()
    {
        const byte Pattern = 0xa5;
        const int Room = 64;

        var compared = 0;

        foreach (V7AvHeader header in Headers())
        {
            byte[] theirs = new byte[Room];
            byte[] ours = new byte[Room];
            Array.Fill(theirs, Pattern);
            Array.Fill(ours, Pattern);

            ChiakiError theirError = NativeAvPacketV7.FormatHeader(theirs, header, out int theirSize);
            ChiakiError ourError = AvPacketV7.FormatHeader(ours, header, out int ourSize);

            Assert.Equal(ChiakiError.Success, theirError);
            Assert.Equal(theirError, ourError);
            Assert.Equal(theirSize, ourSize);
            Assert.Equal(AvPacketV7.HeaderSize(header.IsVideo, header.UsesNaluInfoStructs), ourSize);
            Assert.True(theirs.SequenceEqual(ours), $"{header} formatted differently");

            compared++;
        }

        output.WriteLine($"{compared} headers formatted identically");
        Assert.Equal(20, compared);
    }

    /// <summary>
    /// A buffer too small is refused by both, and both still report the size that would have fitted.
    ///
    /// The C sets header_size_out BEFORE its bound check, which is not tidiness: senkusha.c's MTU
    /// probe asserts the size it expected and only then looks at the error, so a port that wrote the
    /// size only on success would leave that assertion reading whatever was on the stack.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ABufferTooSmallIsRefusedAndTheSizeIsStillReported(bool isVideo, bool nalu)
    {
        int needed = AvPacketV7.HeaderSize(isVideo, nalu);
        var header = new V7AvHeader(isVideo, nalu, 1, 2, 3, 4, 5, 6, 7, 1, 8, 0, 0);

        for (int room = 1; room < needed; room++)
        {
            ChiakiError theirError = NativeAvPacketV7.FormatHeader(
                new byte[room], header, out int theirSize);
            ChiakiError ourError = AvPacketV7.FormatHeader(
                new byte[room], header, out int ourSize);

            Assert.Equal(ChiakiError.BufTooSmall, theirError);
            Assert.Equal(theirError, ourError);
            Assert.Equal(needed, theirSize);
            Assert.Equal(needed, ourSize);
        }

        Assert.Equal(
            ChiakiError.Success,
            AvPacketV7.FormatHeader(new byte[needed], header, out _));
    }

    /// <summary>
    /// THE ROUND TRIP: what the managed formatter writes, the C's own parse reads back.
    ///
    /// The check the two differentials cannot make between them. A formatter and a parse written
    /// from the same wrong reading of the layout would agree with each other perfectly; handing the
    /// bytes to the C's parser is what says the layout is the console's.
    ///
    /// Every field here is inside its width on the wire, so the comparison is equality rather than
    /// masking - except the key position, which has its own check below.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TheFormattersOutputIsReadBackByTheCsOwnParse(bool isVideo, bool nalu)
    {
        var header = new V7AvHeader(
            isVideo, nalu,
            PacketIndex: 0x1234,
            FrameIndex: 0x5678,
            UnitIndex: 0x2ab,
            UnitsInFrameTotal: 0x101,
            UnitsInFrameFec: 0x0cd,
            Codec: 5,
            WordAt0x18: 0x9abc,
            AdaptiveStreamIndex: 3,
            KeyPos: 0,
            DataOffset: 0,
            DataSize: 0);

        byte[] buffer = new byte[AvPacketV7.HeaderSize(isVideo, nalu)];

        Assert.Equal(ChiakiError.Success, AvPacketV7.FormatHeader(buffer, header, out int written));
        Assert.Equal(buffer.Length, written);

        V7AvHeader? read = NativeAvPacketV7.Parse(buffer, out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(read);

        Assert.Equal(isVideo, read.Value.IsVideo);
        Assert.Equal(nalu, read.Value.UsesNaluInfoStructs);
        Assert.Equal(header.PacketIndex, read.Value.PacketIndex);
        Assert.Equal(header.FrameIndex, read.Value.FrameIndex);
        Assert.Equal(header.UnitIndex, read.Value.UnitIndex);
        Assert.Equal(header.UnitsInFrameTotal, read.Value.UnitsInFrameTotal);
        Assert.Equal(header.UnitsInFrameFec, read.Value.UnitsInFrameFec);
        Assert.Equal(header.Codec, read.Value.Codec);

        // The video arm is the only place these two are written, so they come back zero otherwise.
        Assert.Equal(isVideo ? header.WordAt0x18 : (ushort)0, read.Value.WordAt0x18);
        Assert.Equal(isVideo ? header.AdaptiveStreamIndex : (byte)0, read.Value.AdaptiveStreamIndex);

        // Written exactly full, so the payload is empty and starts where the header ends.
        Assert.Equal(buffer.Length, read.Value.DataOffset);
        Assert.Equal(0, read.Value.DataSize);
    }

    /// <summary>
    /// THE ONE FIELD THE FORMATTER DOES NOT BYTE-SWAP, which a round trip is what finds.
    ///
    /// Every other multi-byte write in chiaki_takion_v7_av_packet_format_header goes through htons
    /// or htonl; the key position is a plain store. So it leaves in host order and the parse's ntohl
    /// reads it back reversed, on every machine this port targets.
    ///
    /// Reproduced rather than repaired. The console is what reads these bytes, and both of
    /// senkusha's call sites zero the packet - so the asymmetry has never reached a wire, which is
    /// why nothing has ever noticed it. Asserted through the C's own formatter as well as the
    /// managed one, so this is a fact about libchiaki and not about the port.
    /// </summary>
    [Theory]
    [InlineData(0x00000000u)]
    [InlineData(0x00000001u)]
    [InlineData(0x12345678u)]
    [InlineData(0xdeadbeefu)]
    public void TheKeyPositionIsTheOneFieldThatIsNotByteSwapped(uint keyPos)
    {
        var header = new V7AvHeader(false, false, 1, 2, 3, 4, 5, 6, 0, 0, keyPos, 0, 0);

        byte[] ours = new byte[AvPacketV7.HeaderBase];
        byte[] theirs = new byte[AvPacketV7.HeaderBase];

        Assert.Equal(ChiakiError.Success, AvPacketV7.FormatHeader(ours, header, out _));
        Assert.Equal(ChiakiError.Success, NativeAvPacketV7.FormatHeader(theirs, header, out _));
        Assert.Equal(theirs, ours);

        uint reversed = BinaryPrimitives.ReverseEndianness(keyPos);

        Assert.Equal(reversed, NativeAvPacketV7.Parse(ours, out _)!.Value.KeyPos);
        Assert.Equal(reversed, AvPacketV7.Parse(ours, out _)!.Value.KeyPos);
    }

    /// <summary>
    /// THE FIRST DIFFERENCE: the bound counts the nalu-info add for video as well as audio.
    ///
    /// v9's video constant already reserves those three bytes, which is why PP499's repair added
    /// the term on the audio arm alone. v7 adds it on both, so the same video packet needs three
    /// more bytes here than the arithmetic v9 does - stated as sizes, and then as the two smallest
    /// packets each parser accepts.
    /// </summary>
    [Fact]
    public void TheBoundCountsTheNaluAddForVideoToo()
    {
        Assert.Equal(
            AvPacketV7.HeaderSize(isVideo: true, usesNaluInfoStructs: false) + AvPacketV7.NaluInfoAdd,
            AvPacketV7.HeaderSize(isVideo: true, usesNaluInfoStructs: true));

        // v9's does not move, which is what makes this a difference rather than a shared rule.
        Assert.Equal(
            AvPacketParse.HeaderSize(false, isVideo: true, usesNaluInfo: false),
            AvPacketParse.HeaderSize(false, isVideo: true, usesNaluInfo: true));

        Assert.Null(AvPacketV7.Parse(Packet(VideoWithNaluFlag, 0x17), out ChiakiError tooSmall));
        Assert.Equal(ChiakiError.BufTooSmall, tooSmall);
        Assert.NotNull(AvPacketV7.Parse(Packet(VideoWithNaluFlag, 0x18), out _));
    }

    /// <summary>
    /// AND IT RESERVES NO PAYLOAD BYTE, which v9's <c>+ 1</c> does.
    ///
    /// A packet whose header exactly fills it parses here, with a payload of nothing; the same
    /// packet is refused by v9. The C's own check is the giveaway - <c>buf_size &lt; header_size</c>
    /// against the whole datagram, where v9 compares what follows byte zero and asks for one more.
    /// </summary>
    [Fact]
    public void TheBoundReservesNoPayloadByte()
    {
        byte[] exact = Packet(AudioPlain, AvPacketV7.HeaderBase);

        V7AvHeader? ours = AvPacketV7.Parse(exact, out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(ours);
        Assert.Equal(0, ours.Value.DataSize);
        Assert.Equal(exact.Length, ours.Value.DataOffset);

        Assert.Equal(ours, NativeAvPacketV7.Parse(exact, out _));

        // v9 asks for one more byte than its header, so the same datagram is too small there.
        using var keyState = new KeyState();
        Assert.Null(AvPacketParse.Parse(false, keyState, exact, out ChiakiError v9Error));
        Assert.Equal(ChiakiError.BufTooSmall, v9Error);
    }

    /// <summary>
    /// THE SECOND DIFFERENCE: the packed word always takes the video layout.
    ///
    /// An AUDIO packet parses without complaint in both versions and its three counts come out of
    /// different bits. That is the difference with no marker at the call site - not a refusal, three
    /// wrong numbers - so it is asserted as a disagreement between the two parsers over one
    /// datagram rather than as an offset somewhere.
    /// </summary>
    [Fact]
    public void ThePackedWordTakesTheVideoLayoutForAudioToo()
    {
        byte[] audio = Packet(AudioPlain, 40);

        using var keyState = new KeyState();

        V7AvHeader seven = AvPacketV7.Parse(audio, out _)!.Value;
        AvPacket nine = AvPacketParse.Parse(false, keyState, audio, out _)!.Value;

        output.WriteLine(
            $"v7 {seven.UnitIndex}/{seven.UnitsInFrameTotal}/{seven.UnitsInFrameFec}, "
                + $"v9 {nine.UnitIndex}/{nine.UnitsInFrameTotal}/{nine.UnitsInFrameFec}");

        Assert.NotEqual(nine.UnitIndex, seven.UnitIndex);
        Assert.NotEqual(nine.UnitsInFrameTotal, seven.UnitsInFrameTotal);
        Assert.NotEqual(nine.UnitsInFrameFec, seven.UnitsInFrameFec);

        // And the video arm agrees, so the disagreement above is the layout and not the reader.
        byte[] video = Packet(VideoPlain, 40);

        V7AvHeader sevenVideo = AvPacketV7.Parse(video, out _)!.Value;
        AvPacket nineVideo = AvPacketParse.Parse(false, keyState, video, out _)!.Value;

        Assert.Equal(nineVideo.UnitIndex, sevenVideo.UnitIndex);
        Assert.Equal(nineVideo.UnitsInFrameTotal, sevenVideo.UnitsInFrameTotal);
        Assert.Equal(nineVideo.UnitsInFrameFec, sevenVideo.UnitsInFrameFec);
    }

    /// <summary>
    /// THE THIRD DIFFERENCE: the key position is thirty-two raw bits with no state behind it.
    ///
    /// v9 hands its low half to a ChiakiKeyState, which remembers the high half and increments it
    /// across a wrap. v7 does not, so a position of 0xffffffff stays 0xffffffff and the next packet
    /// does not step it into the second four gigabytes - the same sequence expands one way through
    /// v9 and not at all through v7.
    /// </summary>
    [Fact]
    public void TheKeyPositionIsRawThirtyTwoBitsWithNoState()
    {
        static byte[] WithKeyPos(byte lead, uint keyPos)
        {
            byte[] packet = Packet(lead, 40);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0xe), keyPos);
            return packet;
        }

        Assert.Equal(0xffffffffu, AvPacketV7.Parse(WithKeyPos(AudioPlain, 0xffffffff), out _)!.Value.KeyPos);
        Assert.Equal(0x00000010u, AvPacketV7.Parse(WithKeyPos(AudioPlain, 0x00000010), out _)!.Value.KeyPos);

        // The C agrees, asked in the same order - which is the half that says no ledger moved.
        Assert.Equal(
            0xffffffffu,
            NativeAvPacketV7.Parse(WithKeyPos(AudioPlain, 0xffffffff), out _)!.Value.KeyPos);
        Assert.Equal(
            0x00000010u,
            NativeAvPacketV7.Parse(WithKeyPos(AudioPlain, 0x00000010), out _)!.Value.KeyPos);

        // v9 over the same two, in the same order, carries the wrap.
        using var keyState = new KeyState();

        // v9 reads its low half at av+0xd, which is buf+0xe - the same four bytes.
        AvPacketParse.Parse(false, keyState, WithKeyPos(AudioPlain, 0xffffffff), out _);
        AvPacket after = AvPacketParse.Parse(false, keyState, WithKeyPos(AudioPlain, 0x00000010), out _)!.Value;

        output.WriteLine($"v9 expanded 0x10 after a wrap to 0x{after.KeyPos:x}");
        Assert.True(after.KeyPos > uint.MaxValue, $"v9 did not carry the wrap: 0x{after.KeyPos:x}");
    }

    /// <summary>
    /// THE FOURTH DIFFERENCE, and the one that was not visible until both walks were written out:
    /// v7 has no audio arm.
    ///
    /// v9 steps one byte past the fixed header for audio before the payload starts; v7 goes straight
    /// there. So the same audio datagram's payload begins a byte earlier here - which a port that
    /// reused v9's walk with v7's bound would get wrong by one, on every audio packet, silently.
    /// The video arms are identical, which is what makes this about the arm and not the header.
    /// </summary>
    [Fact]
    public void TheAudioArmIsAbsent()
    {
        byte[] audio = Packet(AudioPlain, 40);
        byte[] video = Packet(VideoPlain, 40);

        using var keyState = new KeyState();

        V7AvHeader sevenAudio = AvPacketV7.Parse(audio, out _)!.Value;
        AvPacket nineAudio = AvPacketParse.Parse(false, keyState, audio, out _)!.Value;

        Assert.Equal(nineAudio.DataOffset - 1, sevenAudio.DataOffset);
        Assert.Equal(nineAudio.DataSize + 1, sevenAudio.DataSize);

        V7AvHeader sevenVideo = AvPacketV7.Parse(video, out _)!.Value;
        AvPacket nineVideo = AvPacketParse.Parse(false, keyState, video, out _)!.Value;

        Assert.Equal(nineVideo.DataOffset, sevenVideo.DataOffset);
        Assert.Equal(nineVideo.DataSize, sevenVideo.DataSize);
    }

    /// <summary>A datagram that is neither audio nor video is refused by kind, not by size.</summary>
    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x01)]
    [InlineData((byte)0x04)]
    [InlineData((byte)0x0f)]
    public void ADatagramThatIsNeitherIsRefusedAsInvalid(byte lead)
    {
        Assert.Null(AvPacketV7.Parse(Packet(lead, 60), out ChiakiError error));
        Assert.Equal(ChiakiError.InvalidData, error);
        Assert.Null(NativeAvPacketV7.Parse(Packet(lead, 60), out ChiakiError theirs));
        Assert.Equal(error, theirs);
    }

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptyBufferSaysNo()
    {
        Assert.Null(AvPacketV7.Parse([], out ChiakiError error));
        Assert.Equal(ChiakiError.BufTooSmall, error);
    }

    /// <summary>
    /// THE OWNERSHIP CLAIM, read out of the C rather than believed: senkusha calls it, takion does not.
    ///
    /// This is what the decision rests on. The formatter is defined in takion.c, one of the three
    /// files PP27's fourth criterion says leave the build, and called twice from senkusha.c, which
    /// is not. A third caller anywhere under lib/src would change where it has to go, so the sweep
    /// is over the directory and not over the two files the answer is expected in.
    /// </summary>
    [Fact]
    public void TheFormatterIsCalledOnlyBySenkusha()
    {
        if (AvPacketV7Source.LocateTakion() is not { } takionPath
            || AvPacketV7Source.LocateSenkusha() is not { } senkushaPath
            || AvPacketV7Source.LocateLibSource() is not { } libSource)
        {
            return;
        }

        string takion = File.ReadAllText(takionPath);
        string senkusha = File.ReadAllText(senkushaPath);

        Assert.Equal(
            AvPacketV7Source.SenkushaCallSites,
            AvPacketV7Source.Calls(senkusha, AvPacketV7Source.FormatterName));

        Assert.Equal(0, AvPacketV7Source.Calls(takion, AvPacketV7Source.FormatterName));

        IReadOnlyList<string> naming =
            AvPacketV7Source.FilesNaming(libSource, AvPacketV7Source.FormatterName);

        output.WriteLine($"named by: {string.Join(", ", naming)}");
        Assert.Equal(new[] { "senkusha.c", "takion.c" }, naming);
    }

    /// <summary>
    /// And the parse is reached by a function pointer the version chooses, never by a call.
    ///
    /// Which is why porting it moves no call site: what selects it is a version number at connect,
    /// and that is the switch a managed transport reproduces.
    /// </summary>
    [Fact]
    public void TheParseIsChosenByVersionAndNeverCalled()
    {
        if (AvPacketV7Source.LocateTakion() is not { } path)
            return;

        Assert.True(AvPacketV7Source.TheParseIsChosenByVersion(File.ReadAllText(path)));
    }

    /// <summary>
    /// Senkusha still formats AUDIO headers only, which is what fixes their size at the base.
    ///
    /// Its own assertion is <c>header_size == MTU_AV_PACKET_ADD</c> and that macro is the base
    /// constant alone. It holds only because both call sites zero the packet and set is_video false,
    /// so neither add is paid - a probe that went video-sized would satisfy every test of the
    /// formatter above and change the MTU senkusha measures.
    /// </summary>
    [Fact]
    public void SenkushaStillFormatsAudioHeadersOnly()
    {
        if (AvPacketV7Source.LocateSenkusha() is not { } path)
            return;

        Assert.True(AvPacketV7Source.SenkushaFormatsAudioHeadersOnly(File.ReadAllText(path)));

        Assert.Equal(
            AvPacketV7.HeaderBase,
            AvPacketV7.HeaderSize(isVideo: false, usesNaluInfoStructs: false));
    }
}
