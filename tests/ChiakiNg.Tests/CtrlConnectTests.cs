using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP356, under PP294: the ctrl request, and the crypt counter it spends before sending anything.
///
/// PP297's capture starts after all of this - the tap's first ctrl entry is a LOGIN - so none of it
/// is witnessed and all of it is asserted against ctrl.c.
/// </summary>
public class CtrlConnectTests
{
    /// <summary>
    /// THE COUNTER THE FIRST MESSAGE IS ENCRYPTED AT, which is not zero and is not fixed.
    ///
    /// Three values always, plus a bitrate from PS4 target 10, plus a streaming type on a PS5, plus
    /// one thrown away on rudp with a PS5. Get it wrong and every ctrl message decrypts to nothing
    /// at the far end, with no local error at all.
    /// </summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_8, false, 3u)]
    [InlineData(ChiakiTarget.Ps4_9, false, 3u)]
    [InlineData(ChiakiTarget.Ps4_10, false, 4u)]
    [InlineData(ChiakiTarget.Ps5_1, false, 5u)]
    [InlineData(ChiakiTarget.Ps5_1, true, 6u)]
    public void TheFirstMessagesCounterDependsOnTargetAndTransport(
        ChiakiTarget target, bool overRudp, uint expected)
    {
        Assert.Equal(expected, CtrlConnect.CounterAfterConnect(target, overRudp));
    }

    /// <summary>
    /// And the extra bump is only on rudp AND a PS5, not either alone.
    /// </summary>
    [Fact]
    public void TheThrownAwayBumpNeedsBothRudpAndAPs5()
    {
        Assert.Equal(
            CtrlConnect.CounterAfterConnect(ChiakiTarget.Ps4_10, overRudp: false),
            CtrlConnect.CounterAfterConnect(ChiakiTarget.Ps4_10, overRudp: true));

        Assert.NotEqual(
            CtrlConnect.CounterAfterConnect(ChiakiTarget.Ps5_1, overRudp: false),
            CtrlConnect.CounterAfterConnect(ChiakiTarget.Ps5_1, overRudp: true));
    }

    /// <summary>The path is the one branch the target decides, and all three are reproduced.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_8, "/sce/rp/session/ctrl")]
    [InlineData(ChiakiTarget.Ps4_9, "/sce/rp/session/ctrl")]
    [InlineData(ChiakiTarget.Ps4_10, "/sie/ps4/rp/sess/ctrl")]
    [InlineData(ChiakiTarget.Ps5_1, "/sie/ps5/rp/sess/ctrl")]
    public void ThePathIsTheOneTheTargetChooses(ChiakiTarget target, string path)
    {
        Assert.Equal(path, CtrlConnect.PathFor(target));
    }

    /// <summary>
    /// The two conditional headers come LAST, because the format string appends them at the end.
    ///
    /// A port emitting them in declaration order would send a request the console reads differently.
    /// </summary>
    [Fact]
    public void TheConditionalHeadersComeLast()
    {
        IReadOnlyList<string> ps5 = CtrlConnect.HeadersFor(ChiakiTarget.Ps5_1);

        Assert.Equal("RP-ConPath", ps5[^3]);
        Assert.Equal("RP-StartBitrate", ps5[^2]);
        Assert.Equal("RP-StreamingType", ps5[^1]);
    }

    /// <summary>And each target sends exactly the headers it should.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_8, false, false)]
    [InlineData(ChiakiTarget.Ps4_10, true, false)]
    [InlineData(ChiakiTarget.Ps5_1, true, true)]
    public void EachTargetSendsTheHeadersItShould(ChiakiTarget target, bool bitrate, bool streaming)
    {
        IReadOnlyList<string> headers = CtrlConnect.HeadersFor(target);

        Assert.Equal(bitrate, headers.Contains("RP-StartBitrate"));
        Assert.Equal(streaming, headers.Contains("RP-StreamingType"));

        // The fixed ones are there whatever the target.
        Assert.Contains("RP-Auth", headers);
        Assert.Contains("RP-Did", headers);
        Assert.Contains("RP-OSType", headers);
    }

    /// <summary>The codec map: H265 is 2, HDR is 3, and anything else is 1.</summary>
    [Theory]
    [InlineData(ChiakiCodec.H264, 1u)]
    [InlineData(ChiakiCodec.H265, 2u)]
    [InlineData(ChiakiCodec.H265Hdr, 3u)]
    public void TheStreamingTypeComesFromTheCodec(ChiakiCodec codec, uint expected)
    {
        Assert.Equal(expected, CtrlConnect.StreamingTypeFor((int)codec));
    }

    /// <summary>
    /// THE STREAMING TYPE IS LITTLE-ENDIAN, alone in a protocol that is otherwise big-endian.
    ///
    /// Four lines from a big-endian message header, in the same function.
    /// </summary>
    [Fact]
    public void TheStreamingTypeIsLittleEndian()
    {
        Assert.Equal<byte[]>([0x02, 0x00, 0x00, 0x00], CtrlConnect.StreamingTypeBytes(2));
        Assert.Equal<byte[]>([0x78, 0x56, 0x34, 0x12], CtrlConnect.StreamingTypeBytes(0x12345678));
    }

    /// <summary>The two literals the request states outright, carried as literals.</summary>
    [Fact]
    public void TheControllerAndClientTypesAreLiterals()
    {
        Assert.Equal(("3", "11"), CtrlConnect.FixedTypes);
    }

    /// <summary>The ostype is what the C sends, and its NUL is part of what is encrypted.</summary>
    [Fact]
    public void TheOsTypeIsWhatTheConsoleIsTold()
    {
        Assert.Equal("Win10.0.0", CtrlConnect.OsType);
    }

    /// <summary>
    /// And ctrl.c still spends the counter the number of times this counts on.
    ///
    /// Counted out of the source, because the bare increment on the rudp PS5 path is the one a
    /// reader skips - it is not next to an encrypt.
    /// </summary>
    [Fact]
    public void CtrlStillSpendsTheCounterFiveTimesAndOnceForNothing()
    {
        string? path = CtrlConnectSource.Locate();
        if (path is null)
            return;

        string? body = CtrlConnectSource.ConnectBody(path);
        Assert.NotNull(body);

        // Five encrypts plus the throwaway: the most any one connect spends is six, and every
        // spend appears in the body once.
        Assert.Equal(
            (int)CtrlConnect.CounterAfterConnect(ChiakiTarget.Ps5_1, overRudp: true),
            CtrlConnectSource.CounterSpendsIn(body));
    }

    /// <summary>And the rest of the connect still looks the way this reproduces it.</summary>
    [Fact]
    public void CtrlStillDeclaresTheConnect()
    {
        string? path = CtrlConnectSource.Locate();
        if (path is null)
            return;

        string? body = CtrlConnectSource.ConnectBody(path);
        Assert.NotNull(body);

        Assert.True(
            CtrlConnectSource.BothCountersStillStartAtZero(body),
            "the crypt counters no longer start at zero, so every counter below is off");
        Assert.True(
            CtrlConnectSource.TheStreamingTypeIsStillLittleEndian(body),
            "the streaming type is no longer assembled from the low byte up");
        Assert.True(
            CtrlConnectSource.TheConditionalHeadersAreStillLast(body),
            "the conditional headers are no longer appended after the fixed ones");
        Assert.True(
            CtrlConnectSource.ThePathsAreStill(body),
            "the three ctrl paths have changed");
    }
}
