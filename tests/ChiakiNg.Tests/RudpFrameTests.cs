using System.Buffers.Binary;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the RUDP frame, its twice-read size field, and the subtype nobody sends.
/// </summary>
public class RudpFrameTests
{
    /// <summary>A frame on the wire, with the size field filled in the way the core fills it.</summary>
    private static byte[] Frame(RudpPacketType type, byte[] data, byte[]? trailing = null)
    {
        var message = new RudpMessage(
            RudpFrame.SizeFor(data.Length), 0, type, 0, data, 0, null);

        byte[] bytes = RudpFrame.Serialise(message);
        return trailing is null ? bytes : [.. bytes, .. trailing];
    }

    private static byte[] Bytes(int count)
        => [.. Enumerable.Range(0, count).Select(i => (byte)(i + 1))];

    /// <summary>An ordinary frame goes out and comes back the same.</summary>
    [Fact]
    public void AFrameRoundTrips()
    {
        byte[] data = Bytes(6);

        RudpMessage? read = RudpFrame.Parse(Frame(RudpPacketType.SessionMessage, data));

        Assert.NotNull(read);
        Assert.Equal(RudpPacketType.SessionMessage, read.Type);
        Assert.Equal(data, read.Data);
        Assert.Null(read.SubMessage);
    }

    /// <summary>The constant sits between the size and the type, where nothing else can.</summary>
    [Fact]
    public void TheConstantIsFourBytesInAtOffsetTwo()
    {
        byte[] bytes = Frame(RudpPacketType.Ack, Bytes(2));

        Assert.Equal(RudpFrame.Constant, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(2)));
    }

    /// <summary>
    /// THE SIZE FIELD IS READ TWICE AND ANSWERS DIFFERENTLY. The top nibble is a 0xC marker, not
    /// length: the core stores the raw value in size, then masks byte zero IN PLACE and reads the
    /// same two bytes again into the length it calculates with.
    ///
    /// A port that read the field once would have one of the two wrong wherever it looked.
    /// </summary>
    [Fact]
    public void TheSameTwoBytesAreBothTheSizeAndTheLength()
    {
        byte[] data = Bytes(6);
        byte[] bytes = Frame(RudpPacketType.SessionMessage, data);

        RudpMessage? read = RudpFrame.Parse(bytes);

        Assert.NotNull(read);

        // One field on the wire, two values out of it.
        Assert.Equal(BinaryPrimitives.ReadUInt16BigEndian(bytes), read.Size);
        Assert.Equal(0xC00E, read.Size);
        Assert.Equal(14, read.Length);
        Assert.Equal(RudpFrame.HeaderSize + data.Length, read.Length);

        // And the marker really is the difference between them.
        Assert.Equal(RudpFrame.SizeMarker << 12, read.Size - read.Length);
    }

    /// <summary>
    /// THE SUBTYPE IS THE TYPE'S HIGH BYTE. It is a field of its own on the struct, filled from the
    /// first of the two bytes the type was just read from - nothing new arrives to carry it.
    /// </summary>
    [Theory]
    [InlineData(RudpPacketType.SessionMessage, 0x20)]
    [InlineData(RudpPacketType.CookieResponse, 0xA0)]
    [InlineData(RudpPacketType.Finish, 0xC0)]
    [InlineData(RudpPacketType.Offset8, 0x12)]
    [InlineData(RudpPacketType.Offset10, 0x26)]
    public void TheSubtypeIsTheTypesHighByte(RudpPacketType type, byte subtype)
    {
        RudpMessage? read = RudpFrame.Parse(Frame(type, Bytes(2)));

        Assert.NotNull(read);
        Assert.Equal(subtype, read.Subtype);
        Assert.Equal((byte)((ushort)type >> 8), read.Subtype);
    }

    /// <summary>
    /// And what makes that field worth having: "a control message" is admitted on the subtype's LOW
    /// NIBBLE being 2 or 6, and FOUR distinct packet types satisfy that - CTRL_MESSAGE (0x0230),
    /// OFFSET8 (0x1230), OFFSET10 (0x2630) and, because its high byte is also 0x02, the member the
    /// enum literally calls UNKNOWN (0x022F).
    ///
    /// So the receive path's idea of a control message is wider than the enum member of that name,
    /// the two offset types - named for where their payload starts - are admitted under it by a
    /// byte nobody sent, and so is the one that means "we do not know what this is". A port matching
    /// on the TYPE would reject three quarters of them.
    /// </summary>
    [Fact]
    public void FourTypesAreAdmittedAsAControlMessage()
    {
        Assert.Equal<IEnumerable<byte>>([0x2, 0x6], RudpFrame.CtrlSubtypeNibbles);

        RudpPacketType[] admitted =
        [
            RudpPacketType.CtrlMessage,
            RudpPacketType.Offset8,
            RudpPacketType.Offset10,
            RudpPacketType.Unknown,
        ];

        foreach (RudpPacketType type in admitted)
        {
            RudpMessage? read = RudpFrame.Parse(Frame(type, Bytes(2)));

            Assert.NotNull(read);
            Assert.Contains((byte)(read.Subtype & 0x0F), RudpFrame.CtrlSubtypeNibbles);
        }

        // And the rest are not, which is what the check is for.
        foreach (RudpPacketType type in Enum.GetValues<RudpPacketType>().Except(admitted))
        {
            RudpMessage? read = RudpFrame.Parse(Frame(type, Bytes(2)));

            Assert.NotNull(read);
            Assert.DoesNotContain((byte)(read.Subtype & 0x0F), RudpFrame.CtrlSubtypeNibbles);
        }
    }

    /// <summary>
    /// THE COUNTER IS THE PAYLOAD'S FIRST TWO BYTES PLUS ONE - not the value sent, the next one.
    /// </summary>
    [Fact]
    public void TheCounterIsOneMoreThanWhatArrived()
    {
        var data = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(data, 41234);

        RudpMessage? read = RudpFrame.Parse(Frame(RudpPacketType.SessionMessage, data));

        Assert.NotNull(read);
        Assert.Equal(41235, read.RemoteCounter);
    }

    /// <summary>
    /// And a payload of fewer than two bytes leaves it at zero - which is indistinguishable from a
    /// peer whose counter really did wrap round to 65535 and send it.
    /// </summary>
    [Fact]
    public void AShortPayloadLeavesTheCounterAtZeroAndSoDoesAWrap()
    {
        RudpMessage? short1 = RudpFrame.Parse(Frame(RudpPacketType.SessionMessage, Bytes(1)));

        Assert.NotNull(short1);
        Assert.Equal(0, short1.RemoteCounter);

        // 65535 + 1 wraps to the same zero, from a payload that is not short at all.
        var wrapped = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(wrapped, ushort.MaxValue);

        RudpMessage? read = RudpFrame.Parse(Frame(RudpPacketType.SessionMessage, wrapped));

        Assert.NotNull(read);
        Assert.Equal(0, read.RemoteCounter);
    }

    /// <summary>
    /// A LENGTH LONGER THAN WHAT ARRIVED IS TRUNCATED, NOT REFUSED. The frame is processed against
    /// fewer bytes than it advertised, with no error anywhere.
    /// </summary>
    [Fact]
    public void AnOverlongLengthIsTruncatedWithoutComplaint()
    {
        byte[] bytes = Frame(RudpPacketType.SessionMessage, Bytes(4));

        // Claim twenty bytes of payload in a frame that carries four.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), RudpFrame.SizeFor(20));

        RudpMessage? read = RudpFrame.Parse(bytes);

        Assert.NotNull(read);
        Assert.Equal(28, read.Length);
        Assert.Equal(4, read.Data.Length);
    }

    /// <summary>
    /// AND ANYTHING EIGHT BYTES OR LONGER LEFT OVER IS ANOTHER FRAME. There is no check that it
    /// looks like one first, so trailing bytes become a sub-message whatever they are.
    /// </summary>
    [Fact]
    public void EightTrailingBytesBecomeASubMessageWhateverTheyAre()
    {
        byte[] rubbish = [0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF];

        RudpMessage? read = RudpFrame.Parse(
            Frame(RudpPacketType.SessionMessage, Bytes(4), trailing: rubbish));

        Assert.NotNull(read);
        Assert.NotNull(read.SubMessage);
        Assert.Equal(0xBEEF, (ushort)read.SubMessage.Type);
    }

    /// <summary>Seven is one too few, and stays unread rather than becoming a short frame.</summary>
    [Fact]
    public void SevenTrailingBytesAreLeftAlone()
    {
        RudpMessage? read = RudpFrame.Parse(
            Frame(RudpPacketType.SessionMessage, Bytes(4), trailing: Bytes(7)));

        Assert.NotNull(read);
        Assert.Null(read.SubMessage);
    }

    /// <summary>A real nested frame round trips, which is what the sub-message is actually for.</summary>
    [Fact]
    public void ANestedFrameRoundTrips()
    {
        byte[] innerData = Bytes(4);
        var inner = new RudpMessage(
            RudpFrame.SizeFor(innerData.Length), 0, RudpPacketType.CtrlMessage, 0, innerData, 0, null);

        byte[] outerData = Bytes(2);
        var outer = new RudpMessage(
            RudpFrame.SizeFor(outerData.Length), 0, RudpPacketType.SessionMessage, 0, outerData, 0, inner);

        RudpMessage? read = RudpFrame.Parse(RudpFrame.Serialise(outer));

        Assert.NotNull(read);
        Assert.Equal(RudpPacketType.SessionMessage, read.Type);
        Assert.Equal(outerData, read.Data);
        Assert.NotNull(read.SubMessage);
        Assert.Equal(RudpPacketType.CtrlMessage, read.SubMessage.Type);
        Assert.Equal(innerData, read.SubMessage.Data);
    }

    /// <summary>Less than a header is the one thing that is not a frame.</summary>
    [Fact]
    public void SevenBytesIsNotAFrame()
        => Assert.Null(RudpFrame.Parse(Bytes(7)));

    /// <summary>A type nobody has heard of parses anyway, and keeps its value.</summary>
    [Fact]
    public void AnUnknownTypeIsCarriedRatherThanRefused()
    {
        RudpMessage? read = RudpFrame.Parse(Frame((RudpPacketType)0x1234, Bytes(2)));

        Assert.NotNull(read);
        Assert.Equal(0x1234, (ushort)read.Type);
        Assert.Equal(0x12, read.Subtype);
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheFramesRulesAreStillTheQtCores()
    {
        string? path = RudpFrameSource.Locate();
        string? header = RudpFrameSource.LocateHeader();
        if (path is null || header is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(RudpFrameSource.TheTypesAreStillTheseValues(File.ReadAllText(header)), "twelve types");
        Assert.True(RudpFrameSource.TheSizeIsStillReadTwice(core), "read, clobbered, read again");
        Assert.True(RudpFrameSource.TheSubtypeIsStillTheTypesHighByte(core), "a byte nobody sent");
        Assert.True(RudpFrameSource.TheCtrlNibblesAreStillTwoAndSix(core), "two and six");
        Assert.True(RudpFrameSource.AnOverlongLengthIsStillTruncated(core), "truncated, not refused");
        Assert.True(RudpFrameSource.LeftoverBytesAreStillAnotherFrame(core), "eight is another frame");
        Assert.True(RudpFrameSource.TheCounterIsStillOneMore(core), "one more than it said");
        Assert.True(RudpFrameSource.TheMarkerIsStillWrittenIn(core), "the marker written in");
    }

    /// <summary>
    /// And the twice-read check earns its green: a core where the clobber had been removed must
    /// turn it red, because that line is the whole difference between the two values.
    /// </summary>
    [Fact]
    public void TheTwiceReadCheckFailsWithoutTheClobber()
    {
        string? path = RudpFrameSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        string tidied = core.Replace(
            "serialized_msg[0] = serialized_msg[0] & 0x0F;", "", StringComparison.Ordinal);

        Assert.NotEqual(core, tidied);
        Assert.False(RudpFrameSource.TheSizeIsStillReadTwice(tidied));
    }
}
