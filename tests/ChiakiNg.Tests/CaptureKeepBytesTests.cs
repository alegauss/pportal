using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP615, under PP27: the capture keeps what it is given, where a caller says so.
///
/// PP612 established that the tap's eighteen bytes are decided in the vendored C, and PP613 built
/// the relay that carries whole datagrams instead. Between them sat a second truncation nobody had
/// looked at: HeadBytes was a const applied inside Offer, so a capture fed by the relay would have
/// thrown the payloads away on the managed side and the relay would have gained nothing.
///
/// The default does not move. PP510's reason for eighteen is still the right one for a capture
/// fed by the tap, and it is the one a run gets unless it asks otherwise.
/// </summary>
public class CaptureKeepBytesTests
{
    private static byte[] Datagram(int length)
        => [.. Enumerable.Range(0, length).Select(i => (byte)(i & 0xff))];

    /// <summary>Unasked, it keeps the tap's width - which is what every existing caller gets.</summary>
    [Fact]
    public void TheDefaultIsStillTheTapsWidth()
    {
        var capture = new TakionTimingCapture();
        Assert.Equal(TakionTimingCapture.HeadBytes, capture.KeepBytes);

        Assert.True(capture.Offer(Datagram(1400), 0));
        Assert.Equal(TakionTimingCapture.HeadBytes, capture.Datagrams[0].Head.Length);

        // And the length is still the datagram's, which is the distinction PP515 fixed.
        Assert.Equal(1400, capture.Datagrams[0].Length);
    }

    /// <summary>
    /// Asked for the whole thing, it keeps the whole thing - which is what the relay is for.
    /// </summary>
    [Fact]
    public void AskedForMoreItKeepsMore()
    {
        var capture = new TakionTimingCapture(keepBytes: 2048);

        Assert.True(capture.Offer(Datagram(1400), 0));

        CapturedDatagram kept = capture.Datagrams[0];
        Assert.Equal(1400, kept.Head.Length);
        Assert.Equal(1400, kept.Length);
        Assert.Equal(Datagram(1400), kept.Head);
    }

    /// <summary>
    /// It never keeps more than arrived, so a generous width does not invent bytes.
    /// </summary>
    [Fact]
    public void ItKeepsNoMoreThanArrived()
    {
        var capture = new TakionTimingCapture(keepBytes: 4096);

        Assert.True(capture.Offer(Datagram(29), 0));
        Assert.Equal(29, capture.Datagrams[0].Head.Length);
    }

    /// <summary>The bounds constructor carries the width too, so a sampled run can ask.</summary>
    [Fact]
    public void TheBoundsConstructorCarriesIt()
    {
        var bounded = new TakionTimingCapture(SampleWindow.Default, keepBytes: 2048);

        Assert.Equal(2048, bounded.KeepBytes);
        Assert.Equal(TakionTimingCapture.HeadBytes, new TakionTimingCapture(SampleWindow.Default).KeepBytes);
    }

    /// <summary>A width of zero or less is refused, rather than recording nothing quietly.</summary>
    [Fact]
    public void AWidthOfNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TakionTimingCapture(keepBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TakionTimingCapture(keepBytes: -1));
    }

    /// <summary>
    /// And the committed corpus is a tap capture, so the default is what produced it.
    ///
    /// The join back to PP608: that file's heads are eighteen because the C handed eighteen over,
    /// and a reader that found them wider would be reading a relay capture instead.
    /// </summary>
    [Fact]
    public void TheCommittedCorpusIsATapCapture()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        Assert.All(datagrams, d => Assert.True(d.Head.Length <= TakionTimingCapture.HeadBytes));
    }
}
