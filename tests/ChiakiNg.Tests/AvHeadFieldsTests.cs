using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP524, under PP27: the AV head's unit counts and codec byte.
///
/// The numbers in these assertions came from a real session and are named as such - what is
/// permanent here is the reader and the two layouts, which a capture cannot check.
/// </summary>
public class AvHeadFieldsTests
{
    private static byte[] Head(int baseType, uint dword2, byte codec)
    {
        var head = new byte[TakionTimingCapture.HeadBytes];
        head[0] = (byte)baseType;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            head.AsSpan(AvHeadFields.Dword2Offset, 4), dword2);
        head[AvHeadFields.CodecOffset] = codec;
        return head;
    }

    /// <summary>
    /// VIDEO AND AUDIO UNPACK THE SAME FOUR BYTES DIFFERENTLY, which is the reader's whole risk.
    ///
    /// One word, two layouts: video takes 11/11/10 bits and audio 8/8/16. Reading video's layout
    /// for an audio packet gives numbers that look plausible and are not, which is the kind of
    /// wrong that survives review.
    /// </summary>
    [Fact]
    public void TheSameWordMeansDifferentThingsOnTheTwoChannels()
    {
        const uint word = 0x0123_4567;

        AvHeadCounts video = Assert.NotNull(AvHeadFields.Read(Head(TakionDispatch.Video, word, 3)));
        AvHeadCounts audio = Assert.NotNull(AvHeadFields.Read(Head(TakionDispatch.Audio, word, 5)));

        Assert.NotEqual(video.UnitIndex, audio.UnitIndex);
        Assert.NotEqual(video.UnitsInFrameTotal, audio.UnitsInFrameTotal);
        Assert.NotEqual(video.UnitsInFrameFec, audio.UnitsInFrameFec);
    }

    /// <summary>The total is the field plus one, on both channels, as the C writes it.</summary>
    [Theory]
    [InlineData(TakionDispatch.Video)]
    [InlineData(TakionDispatch.Audio)]
    public void TheTotalIsTheFieldPlusOne(int baseType)
    {
        AvHeadCounts counts = Assert.NotNull(AvHeadFields.Read(Head(baseType, 0, 0)));

        Assert.Equal(1, counts.UnitsInFrameTotal);
    }

    /// <summary>
    /// A real session's video: one FEC unit per frame, over frames of thirteen to twenty-nine.
    ///
    /// The shape a capture reported, reproduced here so the reader is checked against it. What the
    /// capture cannot do is fail a build, which is why the numbers are here as well as in a file.
    /// </summary>
    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(29)]
    public void OneFecUnitPerVideoFrameReadsBack(int total)
    {
        uint word = ((uint)(total - 1) << 0xa) | 1;

        AvHeadCounts counts = Assert.NotNull(AvHeadFields.Read(Head(TakionDispatch.Video, word, 3)));

        Assert.Equal(total, counts.UnitsInFrameTotal);
        Assert.Equal(1, counts.UnitsInFrameFec);
    }

    /// <summary>Anything that is not AV has no such fields, and neither has a short head.</summary>
    [Fact]
    public void NonAvAndShortHeadsHaveNoCounts()
    {
        Assert.Null(AvHeadFields.Read(Head(TakionDispatch.Control, 0, 0)));
        Assert.Null(AvHeadFields.Read(new byte[AvHeadFields.MinimumHead - 1]));
    }

    /// <summary>
    /// The codec byte the audio receiver demands, and the one the prologue carries.
    ///
    /// Five is what audioreceiver.c requires; 255 is what the pre-cipher packets hold, and they
    /// never reach that guard because PP490's dispatch postpones them. The two values are named
    /// together because the second is only harmless on account of the postpone.
    /// </summary>
    [Fact]
    public void TheAudioCodecIsFiveAndTheProloguesIsNot()
    {
        Assert.Equal(5, AvHeadFields.AudioCodec);

        AvHeadCounts running = Assert.NotNull(
            AvHeadFields.Read(Head(TakionDispatch.Audio, 0, AvHeadFields.AudioCodec)));
        Assert.Equal(AvHeadFields.AudioCodec, running.Codec);

        AvHeadCounts prologue = Assert.NotNull(AvHeadFields.Read(Head(TakionDispatch.Audio, 0, 255)));
        Assert.NotEqual(AvHeadFields.AudioCodec, prologue.Codec);

        // And the branch that keeps it away from the guard.
        Assert.Equal(
            TakionDispatchBranch.Postponed,
            TakionDispatch.Decide(
                TakionDispatch.Audio, macOk: true, enableCrypt: true, cryptAvailable: false).Branch);
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still unpacks the two layouts apart, reads the codec at av+8, and
    /// audioreceiver.c still refuses anything but five.
    /// </summary>
    [Fact]
    public void TheCStillReadsTheseFieldsThisWay()
    {
        if (AvHeadFieldsSource.Locate() is not { } takion)
            return;

        string parse = Assert.IsType<string>(
            AvHeadFieldsSource.ParseBody(File.ReadAllText(takion)));

        Assert.True(AvHeadFieldsSource.TheTwoLayoutsAreStillDifferent(parse));
        Assert.True(AvHeadFieldsSource.TheCodecIsStillAtAvEight(parse));

        if (AvHeadFieldsSource.LocateAudioReceiver() is not { } audio)
            return;

        Assert.True(AvHeadFieldsSource.TheAudioReceiverDemandsFive(File.ReadAllText(audio)));
    }
}
