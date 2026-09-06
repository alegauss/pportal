using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP510, under PP27: the capture a timing run needs, driven by synthetic datagrams.
///
/// Everything here is decidable without a console, which is the point: what needs one is filling
/// the capture, not settling its shape.
/// </summary>
public class TakionTimingCaptureTests
{
    private static byte[] Datagram(int baseType, int length)
    {
        var packet = new byte[length];
        packet[0] = (byte)baseType;
        for (var i = 1; i < length; i++)
            packet[i] = (byte)(i + 0x20);

        return packet;
    }

    /// <summary>Arrival is relative to the first datagram, so no capture carries a wall clock.</summary>
    [Fact]
    public void ArrivalIsRelativeToTheFirstDatagram()
    {
        var capture = new TakionTimingCapture();

        Assert.True(capture.Offer(Datagram(TakionDispatch.Video, 1300), arrivalMicroseconds: 9_000_000));
        Assert.True(capture.Offer(Datagram(TakionDispatch.Video, 1300), arrivalMicroseconds: 9_016_000));

        Assert.Equal(0, capture.Datagrams[0].ArrivalMicroseconds);
        Assert.Equal(16_000, capture.Datagrams[1].ArrivalMicroseconds);
    }

    /// <summary>
    /// Only the head is kept, and the length recorded is the whole datagram's.
    ///
    /// Both halves. Keeping the head is what makes the capture carry no picture; recording the true
    /// length is what makes it a measurement of the stream rather than of the capture.
    /// </summary>
    [Fact]
    public void OnlyTheHeadIsKeptAndTheLengthIsTheWholeDatagrams()
    {
        var capture = new TakionTimingCapture();
        capture.Offer(Datagram(TakionDispatch.Video, 1300), 0);

        CapturedDatagram taken = Assert.Single(capture.Datagrams);

        Assert.Equal(TakionTimingCapture.HeadBytes, taken.Head.Length);
        Assert.Equal(1300, taken.Length);
        Assert.Equal(TakionDispatch.Video, taken.BaseType);
    }

    /// <summary>
    /// The head is long enough for both models that read a datagram's front.
    ///
    /// PP490 decides on byte zero; PP497's furthest read is a video or audio packet's key position,
    /// which ends at eighteen. A shorter head would leave one of the two unable to run.
    /// </summary>
    [Fact]
    public void TheHeadCoversWhatTheTwoModelsRead()
    {
        TakionMacLayout? av = TakionPacketMac.LayoutFor(TakionDispatch.Video);
        Assert.NotNull(av);

        int furthest = av.Value.KeyPosOffset + TakionPacketMac.KeyPosSize;

        // PP742: COVERS, and it used to be exactly. The keep was the MAC gate's furthest read -
        // a video packet's key position ends at eighteen - and AvPacketParse wants the whole AV
        // header before it reads any of it, which is twenty-eight at its longest. So the head is
        // now the further of the two bounds, and both models still find every byte they read.
        Assert.True(
            TakionTimingCapture.HeadBytes >= furthest,
            $"the head keeps {TakionTimingCapture.HeadBytes} bytes and the MAC gate reads to {furthest}");

        Assert.True(TakionTimingCapture.HeadBytes > 0);
    }

    /// <summary>A datagram shorter than the head keeps all of itself and no padding.</summary>
    [Fact]
    public void AShortDatagramKeepsWhatItHas()
    {
        var capture = new TakionTimingCapture();
        capture.Offer(Datagram(TakionDispatch.Control, 5), 0);

        Assert.Equal(5, Assert.Single(capture.Datagrams).Head.Length);
    }

    /// <summary>The count bound closes the capture and later datagrams are counted as missed.</summary>
    [Fact]
    public void TheCountBoundClosesIt()
    {
        var capture = new TakionTimingCapture(limit: 3);

        for (var i = 0; i < 5; i++)
            capture.Offer(Datagram(TakionDispatch.Audio, 200), i * 1000);

        Assert.Equal(CaptureEnd.Full, capture.End);
        Assert.Equal(3, capture.Datagrams.Count);
        Assert.Equal(2, capture.Missed);
    }

    /// <summary>
    /// And the duration bound closes it too, which is why there are two.
    ///
    /// A count alone captures a burst and calls it a second; a duration alone captures whatever a
    /// quiet link gave. Either bound reached ends the capture.
    /// </summary>
    [Fact]
    public void TheDurationBoundClosesItToo()
    {
        var capture = new TakionTimingCapture(limit: 1000, windowMicroseconds: 10_000);

        Assert.True(capture.Offer(Datagram(TakionDispatch.Video, 900), 0));
        Assert.True(capture.Offer(Datagram(TakionDispatch.Video, 900), 10_000));
        Assert.False(capture.Offer(Datagram(TakionDispatch.Video, 900), 10_001));

        Assert.Equal(CaptureEnd.Elapsed, capture.End);
        Assert.Equal(2, capture.Datagrams.Count);
    }

    /// <summary>
    /// The spacings are one shorter than the capture, and a capture of one has none.
    ///
    /// Reporting a zero for a single datagram would be a measurement nobody took, and the spacings
    /// are what a timing run actually compares.
    /// </summary>
    [Fact]
    public void TheSpacingsAreOneShorterAndOneDatagramHasNone()
    {
        var capture = new TakionTimingCapture();
        foreach (long at in (long[])[0, 16_000, 33_000])
            capture.Offer(Datagram(TakionDispatch.Video, 1200), at);

        Assert.Equal([16_000L, 17_000L], capture.InterArrivalMicroseconds());

        var single = new TakionTimingCapture();
        single.Offer(Datagram(TakionDispatch.Video, 1200), 0);
        Assert.Empty(single.InterArrivalMicroseconds());
    }

    /// <summary>The base types are counted, which is the first thing a run would want.</summary>
    [Fact]
    public void TheBaseTypesAreCounted()
    {
        var capture = new TakionTimingCapture();
        capture.Offer(Datagram(TakionDispatch.Video, 1300), 0);
        capture.Offer(Datagram(TakionDispatch.Video, 1300), 1000);
        capture.Offer(Datagram(TakionDispatch.Audio, 300), 2000);

        IReadOnlyDictionary<int, int> counts = capture.ByBaseType();

        Assert.Equal(2, counts[TakionDispatch.Video]);
        Assert.Equal(1, counts[TakionDispatch.Audio]);
        Assert.False(counts.ContainsKey(TakionDispatch.Control));
    }

    /// <summary>An empty datagram is not captured and does not close the capture either.</summary>
    [Fact]
    public void AnEmptyDatagramIsNotCaptured()
    {
        var capture = new TakionTimingCapture();

        Assert.False(capture.Offer([], 0));
        Assert.Empty(capture.Datagrams);
        Assert.Equal(CaptureEnd.Open, capture.End);
        Assert.Equal(0, capture.Missed);
    }

    /// <summary>
    /// A captured head is decidable by the dispatch, which is the join this exists for.
    ///
    /// If the capture kept something the models could not read, the session that filled it would
    /// have been spent producing a file nothing could measure.
    /// </summary>
    [Fact]
    public void EveryCapturedHeadIsEnoughForTheDispatch()
    {
        var capture = new TakionTimingCapture();
        foreach (int baseType in (int[])[TakionDispatch.Control, TakionDispatch.Video, TakionDispatch.Audio])
            capture.Offer(Datagram(baseType, 800), baseType * 1000);

        foreach (CapturedDatagram taken in capture.Datagrams)
        {
            Assert.Equal(taken.BaseType, TakionDispatch.BaseTypeOf(taken.Head));

            TakionDispatchVerdict verdict = TakionDispatch.Decide(
                taken.BaseType, macOk: true, enableCrypt: true, cryptAvailable: true);

            Assert.NotEqual(TakionDispatchBranch.UnknownType, verdict.Branch);
        }
    }
}
