using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP499, under PP27: the audio bound in av_packet_parse, which stopped one skip short.
///
/// This runs the real parser through the shim rather than reading its text, because the failure was
/// arithmetic and the repair is arithmetic: a size_t going negative. A source check would say the
/// term is there; only running it says the term is enough.
/// </summary>
public class AvHeaderBoundTests
{
    /// <summary>TAKION_PACKET_TYPE_AUDIO with the nalu-info flag in bit 4.</summary>
    private const byte AudioWithNaluFlag = 0x13;

    /// <summary>The same without it.</summary>
    private const byte AudioPlain = 0x03;

    /// <summary>Video carries the flag in the same place, and its constant already reserves it.</summary>
    private const byte VideoWithNaluFlag = 0x12;

    /// <summary>A packet of <paramref name="size"/> bytes led by <paramref name="firstByte"/>.</summary>
    private static byte[] Packet(byte firstByte, int size)
    {
        var packet = new byte[size];
        packet[0] = firstByte;
        for (var i = 1; i < size; i++)
            packet[i] = (byte)(i + 0x10);

        return packet;
    }

    /// <summary>
    /// THE REPAIR: an audio packet claiming nalu-info structs it has no room for is refused.
    ///
    /// Twenty and twenty-one bytes are the two lengths that used to get through - the bound let
    /// them in, and the skip then took av_size below zero. A size_t below zero is SIZE_MAX-1, which
    /// arrived at the audio callback as a frame length.
    ///
    /// Measured with the guard disabled: 20 parsed as DataOffset 22, DataSize -2, and 21 as
    /// DataOffset 22, DataSize -1 - the shim narrowing that size_t to int32. Both name a payload
    /// starting two bytes past a buffer that had already ended.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    public void AnAudioPacketWithNoRoomForItsNaluInfoIsRefused(int size)
    {
        using var keyState = new KeyState();

        AvPacket? parsed = Takion.ParseV9(keyState, Packet(AudioWithNaluFlag, size), out ChiakiError error);

        Assert.Null(parsed);
        Assert.Equal(ChiakiError.BufTooSmall, error);
    }

    /// <summary>
    /// And the same length WITHOUT the flag still parses, so the repair rejects the claim and not
    /// the size.
    ///
    /// This is the half that would catch an over-broad fix: adding the three bytes unconditionally
    /// would turn these into refusals too.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    public void TheSameLengthWithoutTheFlagStillParses(int size)
    {
        using var keyState = new KeyState();

        AvPacket? parsed = Takion.ParseV9(keyState, Packet(AudioPlain, size), out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(parsed);
        Assert.False(parsed.Value.IsVideo);
    }

    /// <summary>
    /// Where the audio bound now sits with the flag: 22 refused, 23 accepted with one data byte.
    ///
    /// 23 is 1 + 0x11 + 1 + 3 + 1 - the lead byte, the fixed header, the audio arm, the skip, and
    /// the one payload byte the bound's `+ 1` exists to guarantee. Without the flag the same
    /// packet is accepted three bytes earlier, which is the whole of the repair.
    /// </summary>
    [Fact]
    public void TheAudioBoundWithTheFlagSitsAtTwentyThree()
    {
        using var keyState = new KeyState();

        Assert.Null(Takion.ParseV9(keyState, Packet(AudioWithNaluFlag, 22), out ChiakiError tooSmall));
        Assert.Equal(ChiakiError.BufTooSmall, tooSmall);

        AvPacket? parsed = Takion.ParseV9(keyState, Packet(AudioWithNaluFlag, 23), out ChiakiError error);

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.Value.DataSize);
        Assert.Equal(22, parsed.Value.DataOffset);
    }

    /// <summary>
    /// No audio length at all yields a data size larger than the packet it came from.
    ///
    /// The general statement of the bug, swept rather than sampled: before the repair, 20 and 21
    /// with the flag produced SIZE_MAX-1. Sweeping is what says there is no third length.
    /// </summary>
    [Fact]
    public void NoAudioPacketEverReportsMoreDataThanItHas()
    {
        using var keyState = new KeyState();

        for (var size = 1; size <= 64; size++)
        {
            foreach (byte lead in new[] { AudioPlain, AudioWithNaluFlag })
            {
                AvPacket? parsed = Takion.ParseV9(keyState, Packet(lead, size), out ChiakiError error);

                if (error != ChiakiError.Success)
                    continue;

                Assert.NotNull(parsed);
                Assert.InRange(parsed.Value.DataSize, 0, size);
                Assert.InRange(parsed.Value.DataOffset, 0, size);
                Assert.True(
                    parsed.Value.DataOffset + parsed.Value.DataSize <= size,
                    $"lead {lead:x2} size {size}: data runs to "
                        + $"{parsed.Value.DataOffset + parsed.Value.DataSize}");
            }
        }
    }

    /// <summary>And the same sweep for video, whose constant already reserved the three bytes.</summary>
    [Fact]
    public void NoVideoPacketEverReportsMoreDataThanItHas()
    {
        using var keyState = new KeyState();

        for (var size = 1; size <= 64; size++)
        {
            AvPacket? parsed = Takion.ParseV9(keyState, Packet(VideoWithNaluFlag, size), out ChiakiError error);

            if (error != ChiakiError.Success)
                continue;

            Assert.NotNull(parsed);
            Assert.True(parsed.Value.IsVideo);
            Assert.True(
                parsed.Value.DataOffset + parsed.Value.DataSize <= size,
                $"size {size}: data runs to {parsed.Value.DataOffset + parsed.Value.DataSize}");
        }
    }

    /// <summary>
    /// Video's own bound is untouched, which is the claim that the term was added to one arm only.
    ///
    /// 25 bytes with the flag is the smallest video packet the parser accepts, before and after the
    /// repair. Adding the three bytes for video too would have moved this to 28.
    /// </summary>
    [Fact]
    public void VideosSmallestAcceptedPacketIsUnchanged()
    {
        using var keyState = new KeyState();

        Assert.Null(Takion.ParseV9(keyState, Packet(VideoWithNaluFlag, 24), out ChiakiError tooSmall));
        Assert.Equal(ChiakiError.BufTooSmall, tooSmall);

        Assert.NotNull(Takion.ParseV9(keyState, Packet(VideoWithNaluFlag, 25), out ChiakiError ok));
        Assert.Equal(ChiakiError.Success, ok);
    }

    /// <summary>
    /// The C's own bound now names the same constant the v7 parser has always used.
    ///
    /// A source check on top of the runtime ones, because the point is that one file stopped doing
    /// this two ways: if the term is ever spelled with a bare 3, this says so.
    /// </summary>
    [Fact]
    public void TheBoundUsesTheV7ConstantAndOnlyForAudio()
    {
        if (SanitizerSource.LocateRelative(TakionPostpone.RelativePath) is not { } path)
            return;

        string body = Assert.IsType<string>(
            CFunction.Body(File.ReadAllText(path), "static ChiakiErrorCode av_packet_parse"));

        Assert.Contains(
            "if(packet->uses_nalu_info_structs && !packet->is_video)", body, StringComparison.Ordinal);
        Assert.Contains(
            "av_header_size += CHIAKI_TAKION_V7_AV_HEADER_SIZE_NALU_INFO_STRUCTS_ADD;",
            body,
            StringComparison.Ordinal);
    }
}
