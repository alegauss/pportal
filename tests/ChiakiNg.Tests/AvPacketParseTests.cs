using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP668: the AV header parsed in managed code, held against the shim on v9 and extended to v12.
///
/// Every AvPacket in the port was built from the shim's v9 export, and the C writes is_haptics
/// exactly once - under <c>if(v12 &amp;&amp; !packet->is_video)</c>. So the bit was false for every
/// packet the port could see, and PP667's route, which tests haptics before audio as PP366's third
/// check demands, had an arm that could never fire.
///
/// THE V9 ARM IS THE ORACLE, and that is the whole method here. The managed parse walks the same
/// bytes as av_packet_parse for both versions, so its v9 answer must equal the shim's on every
/// input - a differential over the real parser rather than a reading of it. That is what makes the
/// v12 arm trustworthy on a machine with no v12 corpus: the only difference between the two arms is
/// one byte, and everything around it is proven identical.
/// </summary>
public class AvPacketParseTests(ITestOutputHelper output)
{
    private const byte AudioPlain = 0x03;
    private const byte AudioWithNaluFlag = 0x13;
    private const byte VideoPlain = 0x02;
    private const byte VideoWithNaluFlag = 0x12;

    /// <summary>A packet of a size, led by a type byte, with distinguishable bytes after it.</summary>
    private static byte[] Packet(byte firstByte, int size, byte? at = null, int atOffset = 0)
    {
        var packet = new byte[size];
        packet[0] = firstByte;

        for (int i = 1; i < size; i++)
            packet[i] = (byte)(i + 0x10);

        if (at is { } value && atOffset < size)
            packet[atOffset] = value;

        return packet;
    }

    /// <summary>
    /// THE DIFFERENTIAL: the managed v9 parse and the shim's agree, field for field.
    ///
    /// Every length from below the bound to well past it, on all four lead bytes, which is 4 times
    /// 60 packets through both parsers. A separate KeyState for each, because requesting a position
    /// commits to a ledger and one parse would otherwise move the other's answer.
    /// </summary>
    [Theory]
    [InlineData(AudioPlain)]
    [InlineData(AudioWithNaluFlag)]
    [InlineData(VideoPlain)]
    [InlineData(VideoWithNaluFlag)]
    public void TheManagedV9ParseAgreesWithTheShim(byte lead)
    {
        int agreed = 0, refusedBoth = 0;

        for (int size = 1; size <= 60; size++)
        {
            byte[] bytes = Packet(lead, size);

            using var forShim = new KeyState();
            using var forManaged = new KeyState();

            AvPacket? shim = Takion.ParseV9(forShim, (byte[])bytes.Clone(), out ChiakiError shimError);
            AvPacket? managed = AvPacketParse.Parse(false, forManaged, bytes, out ChiakiError managedError);

            Assert.Equal(shimError, managedError);

            if (shim is null)
            {
                Assert.Null(managed);
                refusedBoth++;
                continue;
            }

            Assert.NotNull(managed);
            Assert.Equal(shim.Value, managed.Value);
            agreed++;
        }

        output.WriteLine($"lead 0x{lead:x2}: {agreed} agreed, {refusedBoth} refused by both");

        // PP271: a comparison against nothing matches. Both outcomes have to occur, or this passed
        // by refusing everything or by never reaching the bound.
        Assert.True(agreed > 0, "no length parsed, so the agreement above is about refusals only");
        Assert.True(refusedBoth > 0, "no length was refused, so the bound was never exercised");
    }

    /// <summary>
    /// THE POINT OF THE LINE: a v12 audio packet carrying 0x02 is haptics.
    ///
    /// The byte sits after the fixed header, the audio arm's one byte, and the nalu-info skip where
    /// the flag is set. Written out by offset rather than by a constant, so a wrong offset in the
    /// parse fails here rather than reading whatever happened to be there.
    /// </summary>
    [Theory]
    [InlineData(AudioPlain, 1 + AvPacketParse.FixedHeader + 1)]
    [InlineData(AudioWithNaluFlag, 1 + AvPacketParse.FixedHeader + 1 + AvPacketParse.NaluInfoAdd)]
    public void AV12AudioPacketWithTheMarkerIsHaptics(byte lead, int markerAt)
    {
        using var keyState = new KeyState();

        AvPacket? parsed = AvPacketParse.Parse(
            true, keyState, Packet(lead, 40, AvPacketParse.HapticsMarker, markerAt), out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(parsed);
        Assert.False(parsed.Value.IsVideo);
        Assert.True(parsed.Value.IsHaptics, $"the marker at {markerAt} did not reach the parse");
    }

    /// <summary>And any other value there is not haptics, so the test above is about the byte.</summary>
    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x01)]
    [InlineData((byte)0x03)]
    [InlineData((byte)0xff)]
    public void AnyOtherValueIsNotHaptics(byte marker)
    {
        using var keyState = new KeyState();

        AvPacket? parsed = AvPacketParse.Parse(
            true, keyState, Packet(AudioPlain, 40, marker, 1 + AvPacketParse.FixedHeader + 1), out _);

        Assert.NotNull(parsed);
        Assert.False(parsed.Value.IsHaptics);
    }

    /// <summary>
    /// The bit is v12 AND audio, which is the condition the C writes and the reason it was dead.
    ///
    /// A v9 audio packet with 0x02 in the same place is not haptics, and neither is a v12 VIDEO
    /// packet - the C's guard is a conjunction and reading it as either half would set the bit on
    /// packets the console never marked.
    /// </summary>
    [Theory]
    [InlineData(false, AudioPlain)]
    [InlineData(true, VideoPlain)]
    [InlineData(false, VideoPlain)]
    public void OnlyV12AudioEverCarriesTheBit(bool v12, byte lead)
    {
        using var keyState = new KeyState();

        AvPacket? parsed = AvPacketParse.Parse(
            v12, keyState, Packet(lead, 40, AvPacketParse.HapticsMarker, 1 + AvPacketParse.FixedHeader + 1), out _);

        Assert.NotNull(parsed);
        Assert.False(parsed.Value.IsHaptics);
    }

    /// <summary>
    /// v12 audio takes one more byte of header than v9, which is what the extra byte costs.
    ///
    /// 0x13 against 0x12, and the payload starts one later. Both halves matter: a parse that took
    /// the byte without widening the bound would read past a packet at the old limit.
    /// </summary>
    [Fact]
    public void TheV12AudioHeaderIsOneLongerAndThePayloadStartsLater()
    {
        Assert.Equal(AvPacketParse.V9AudioHeader + 1, AvPacketParse.V12AudioHeader);
        Assert.Equal(AvPacketParse.V9VideoHeader, AvPacketParse.V12VideoHeader);

        using var forV9 = new KeyState();
        using var forV12 = new KeyState();

        byte[] bytes = Packet(AudioPlain, 40);

        AvPacket v9 = AvPacketParse.Parse(false, forV9, bytes, out _)!.Value;
        AvPacket v12 = AvPacketParse.Parse(true, forV12, bytes, out _)!.Value;

        Assert.Equal(v9.DataOffset + 1, v12.DataOffset);
        Assert.Equal(v9.DataSize - 1, v12.DataSize);
    }

    /// <summary>
    /// PP499's bound, carried: an audio packet with no room for its nalu-info is refused.
    ///
    /// Twenty and twenty-one are the lengths that used to get through in the C and made av_size go
    /// below zero. The managed parse would produce a negative DataSize instead of a SIZE_MAX, which
    /// is a different wrong answer to the same defect - so the term is here and this says so.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    public void AnAudioPacketWithNoRoomForItsNaluInfoIsRefused(int size)
    {
        using var keyState = new KeyState();

        Assert.Null(AvPacketParse.Parse(
            false, keyState, Packet(AudioWithNaluFlag, size), out ChiakiError error));

        Assert.Equal(ChiakiError.BufTooSmall, error);
    }

    /// <summary>And twenty-three parses, so the bound is a boundary rather than a wall.</summary>
    [Fact]
    public void TwentyThreeIsTheFirstThatParses()
    {
        using var keyState = new KeyState();

        AvPacket? parsed = AvPacketParse.Parse(
            false, keyState, Packet(AudioWithNaluFlag, 23), out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(parsed);
        Assert.True(parsed.Value.DataSize >= 0);
    }

    /// <summary>The video arm's constant already reserves the skip, so it is not added twice.</summary>
    [Fact]
    public void TheVideoArmDoesNotPayTheTermTwice()
    {
        Assert.Equal(
            AvPacketParse.V9VideoHeader,
            AvPacketParse.HeaderSize(false, isVideo: true, usesNaluInfo: true));

        Assert.Equal(
            AvPacketParse.V9AudioHeader + AvPacketParse.NaluInfoAdd,
            AvPacketParse.HeaderSize(false, isVideo: false, usesNaluInfo: true));
    }

    /// <summary>A datagram that is neither audio nor video is refused by kind, not by size.</summary>
    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x01)]
    [InlineData((byte)0x04)]
    [InlineData((byte)0x0f)]
    public void ADatagramThatIsNeitherIsRefusedAsInvalid(byte lead)
    {
        using var keyState = new KeyState();

        Assert.Null(AvPacketParse.Parse(true, keyState, Packet(lead, 60), out ChiakiError error));
        Assert.Equal(ChiakiError.InvalidData, error);
    }

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptyBufferSaysNo()
    {
        using var keyState = new KeyState();

        Assert.Null(AvPacketParse.Parse(true, keyState, [], out ChiakiError error));
        Assert.Equal(ChiakiError.BufTooSmall, error);
    }

    /// <summary>And a null key state is refused rather than dereferenced.</summary>
    [Fact]
    public void ANullKeyStateIsRefused()
        => Assert.Throws<ArgumentNullException>(
            () => AvPacketParse.Parse(true, null!, new byte[40], out _));
}
