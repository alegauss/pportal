using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP511, under PP27: the fifth tap channel, driven through lib/src's own emit.
///
/// chiaki_message_tap_emit is exported so a caller can drive it without a console, which is what
/// makes this checkable at all - the alternative is a session, and PP501 declared why there is not
/// one here.
/// </summary>
[Collection(nameof(MessageTapTests))]
public class TakionDatagramTapTests
{
    private static byte[] Datagram(int baseType, int length)
    {
        var packet = new byte[length];
        packet[0] = (byte)baseType;
        for (var i = 1; i < length; i++)
            packet[i] = (byte)(i + 0x30);

        return packet;
    }

    /// <summary>A clock a test drives, in microseconds.</summary>
    private sealed class Clock
    {
        public long Now { get; set; }

        public long Read() => Now;
    }

    /// <summary>
    /// A datagram emitted through lib/src lands in the capture, at the clock's reading.
    ///
    /// The head is what the emit hands over, so the capture's own truncation has nothing left to do
    /// - which is the point of truncating at the emit.
    /// </summary>
    [Fact]
    public void AnEmittedDatagramLandsInTheCapture()
    {
        var capture = new TakionTimingCapture();
        var clock = new Clock { Now = 5_000_000 };

        using (new TakionDatagramTap(capture, clock.Read))
        {
            byte[] head = Datagram(TakionDispatch.Video, ChiakiMessageTap.TakionHeadBytes);
            ChiakiMessageTap.Emit(
                ExchangeTapDirection.Received,
                ChiakiMessageTap.TakionChannel,
                (ushort)TakionDispatch.Video,
                head);

            clock.Now = 5_016_000;
            ChiakiMessageTap.Emit(
                ExchangeTapDirection.Received,
                ChiakiMessageTap.TakionChannel,
                (ushort)TakionDispatch.Video,
                head);
        }

        Assert.Equal(2, capture.Datagrams.Count);
        Assert.Equal(0, capture.Datagrams[0].ArrivalMicroseconds);
        Assert.Equal(16_000, capture.Datagrams[1].ArrivalMicroseconds);
        Assert.Equal(TakionDispatch.Video, capture.Datagrams[0].BaseType);
    }

    /// <summary>
    /// The other four channels are passed over and counted, not captured.
    ///
    /// One tap is installed at a time - a second Install replaces the first - so filtering here is
    /// the only way both kinds of recording can run in one session.
    /// </summary>
    [Fact]
    public void TheOtherChannelsArePassedOver()
    {
        var capture = new TakionTimingCapture();
        var clock = new Clock();

        using var tap = new TakionDatagramTap(capture, clock.Read);

        foreach (string channel in new[]
                 {
                     ChiakiMessageTap.CtrlChannel,
                     ChiakiMessageTap.SessionChannel,
                     ChiakiMessageTap.SenkushaChannel,
                     ChiakiMessageTap.StreamChannel,
                 })
        {
            ChiakiMessageTap.Emit(ExchangeTapDirection.Received, channel, 1, [1, 2, 3]);
        }

        Assert.Empty(capture.Datagrams);
        Assert.Equal(4, tap.OtherChannels);
    }

    /// <summary>Disposing stops the tap, and the capture keeps what it took.</summary>
    [Fact]
    public void DisposingStopsItAndKeepsWhatItTook()
    {
        var capture = new TakionTimingCapture();
        var clock = new Clock();

        var tap = new TakionDatagramTap(capture, clock.Read);
        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Received, ChiakiMessageTap.TakionChannel, 2, Datagram(2, 18));
        tap.Dispose();

        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Received, ChiakiMessageTap.TakionChannel, 2, Datagram(2, 18));

        Assert.Single(capture.Datagrams);
        Assert.False(ChiakiMessageTap.Active);
    }

    /// <summary>
    /// THE PLACEMENT: the C emits above the MAC gate, behind the active check, truncated.
    ///
    /// All three in one test because they are one decision. Above the gate is what makes a rejected
    /// packet an arrival; behind the check is what makes an untapped session pay a load and a
    /// branch; truncated at the emit is what makes the head's length true for every consumer.
    /// </summary>
    [Fact]
    public void TheCEmitsAboveTheGateGuardedAndTruncated()
    {
        if (TakionDatagramTapSource.Locate() is not { } path)
            return;

        string handle = Assert.IsType<string>(
            TakionDatagramTapSource.HandleBody(File.ReadAllText(path)));

        Assert.True(TakionDatagramTapSource.TheEmitIsAboveTheMacGate(handle));
        Assert.True(TakionDatagramTapSource.TheEmitIsGuardedByTheActiveCheck(handle));
        Assert.True(TakionDatagramTapSource.TheHeadIsTruncatedAtTheEmit(handle));
    }

    /// <summary>
    /// The channel's name and the head's length are the header's, not this port's.
    ///
    /// A managed constant that drifted from the define would install a tap for a channel nothing
    /// emits on, and every capture would come back empty with nothing saying why.
    /// </summary>
    [Fact]
    public void TheChannelAndTheHeadAreTheHeaders()
    {
        if (TakionDatagramTapSource.LocateHeader() is not { } path)
            return;

        string header = File.ReadAllText(path);

        Assert.Equal(ChiakiMessageTap.TakionChannel, TakionDatagramTapSource.ChannelIn(header));
        Assert.Equal(
            (long?)ChiakiMessageTap.TakionHeadBytes, TakionDatagramTapSource.HeadBytesIn(header));
    }

    /// <summary>
    /// And PP510's head is the same number, so the capture keeps exactly what crosses.
    ///
    /// Two constants derived independently - one from the MAC gate's furthest read, one from the C
    /// header - and they have to agree or the capture pads or truncates what the emit already sized.
    /// </summary>
    [Fact]
    public void TheEmitsHeadIsTheCapturesHead()
        => Assert.Equal(TakionTimingCapture.HeadBytes, ChiakiMessageTap.TakionHeadBytes);
}
