using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP676: the microphone packet's head, held against audiosender.c.
///
/// The third send outside PP497's MAC table, and the only one this port cannot drive: the head is
/// built inside a callback needing an opus encoder, a takion and an audio sender, so there is no
/// export to compare bytes with. What is asserted instead is the transcription - the offsets, the
/// byte order and the one extra byte - against the C that states them.
/// </summary>
public class MicPacketHeadTests
{
    private static string? Source()
        => MicPacketHead.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>THE OFFSETS: every field this transcribes is still written where it says.</summary>
    [Fact]
    public void TheCStillWritesEveryFieldWhereThisSaysItDoes()
    {
        if (Source() is not { } source)
            return;

        IReadOnlyList<string> missing = MicPacketHead.OffsetsMissingFrom(source);

        Assert.True(
            missing.Count == 0,
            "audiosender.c no longer writes these where MicPacketHead puts them:\n  "
                + string.Join("\n  ", missing));
    }

    /// <summary>
    /// AND THE THREE ARE STILL RAW, which is the claim the whole file rests on.
    ///
    /// The feedback head next to this one puts every field through htons or htonl. These three do
    /// not, so this port writes them little-endian - and a C that started swapping them would make
    /// that wrong with nothing else noticing.
    /// </summary>
    [Fact]
    public void TheThreeNativeOrderFieldsAreStillNativeOrder()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            MicPacketHead.TheThreeAreStillWrittenRaw(source),
            "audiosender.c no longer writes packet_index, frame_index and units_number as raw "
                + "stores, so MicPacketHead's little-endian writes are describing something else");
    }

    /// <summary>And the head is still one byte longer on a PS5, which moves the payload.</summary>
    [Fact]
    public void TheHeadIsStillLongerOnAPs5()
    {
        if (Source() is not { } source)
            return;

        Assert.True(MicPacketHead.TheHeadIsStillLongerOnAPs5(source));
        Assert.Equal(MicPacketHead.Size + 1, MicPacketHead.SizePs5);

        // The same byte from the other end: TakionFeedbackSends reads it as where the payload
        // starts, and the two have to agree or the encryption covers the wrong bytes.
        Assert.Equal(MicPacketHead.Size, TakionFeedbackSends.Microphone.HeadSize);
        Assert.Equal(MicPacketHead.SizePs5, TakionFeedbackSends.MicrophonePs5.HeadSize);
    }

    /// <summary>
    /// The head this port writes, field by field, with the two orders side by side.
    ///
    /// Written out rather than compared against the C, because there is nothing to compare against -
    /// so the bytes are stated where a reader can check them against the C by eye, which is what
    /// the offsets check above then keeps true.
    /// </summary>
    [Fact]
    public void TheHeadIsWrittenWithBothOrders()
    {
        byte[] head = new byte[MicPacketHead.SizePs5];

        MicPacketHead.Write(
            head, packetType: 0x02, packetIndex: 0x1234, frameIndex: 0x5678,
            unitsNumber: 0x9abcdef0, codec: 0x05, ps5: true);

        Assert.Equal(0x02, head[0]);

        // Little-endian, which is the C's raw store.
        Assert.Equal<byte[]>([0x34, 0x12], head[1..3]);
        Assert.Equal<byte[]>([0x78, 0x56], head[3..5]);
        Assert.Equal<byte[]>([0xf0, 0xde, 0xbc, 0x9a], head[5..9]);

        Assert.Equal(0x05, head[9]);

        // The MAC and the key position are left for the send, which writes them big-endian.
        Assert.Equal<byte[]>([0, 0, 0, 0], head[10..14]);
        Assert.Equal<byte[]>([0, 0, 0, 0], head[14..18]);

        Assert.Equal(0, head[18]);
        Assert.Equal(0, head[19]);
    }

    /// <summary>A PS4 head is nineteen bytes and does not touch the twentieth.</summary>
    [Fact]
    public void APs4HeadLeavesTheTwentiethByteAlone()
    {
        byte[] head = new byte[MicPacketHead.SizePs5];
        head[19] = 0xff;

        MicPacketHead.Write(
            head, packetType: 0x02, packetIndex: 1, frameIndex: 2,
            unitsNumber: 3, codec: 4, ps5: false);

        Assert.Equal(0xff, head[19]);
    }

    /// <summary>A buffer that cannot hold the head is refused rather than written past.</summary>
    [Fact]
    public void AShortBufferIsRefused()
    {
        byte[] tooShort = new byte[MicPacketHead.Size - 1];

        Assert.Throws<ArgumentException>(
            () => MicPacketHead.Write(tooShort, 0, 0, 0, 0, 0, ps5: false));

        byte[] ps4Sized = new byte[MicPacketHead.Size];

        Assert.Throws<ArgumentException>(
            () => MicPacketHead.Write(ps4Sized, 0, 0, 0, 0, 0, ps5: true));
    }

    /// <summary>And the reader finds the file, so none of the above is green over nothing.</summary>
    [Fact]
    public void TheReaderFindsTheC()
    {
        Assert.NotEmpty(MicPacketHead.OffsetsMissingFrom("int main(void) { return 0; }"));
        Assert.False(MicPacketHead.TheThreeAreStillWrittenRaw("int main(void) { return 0; }"));
    }
}
