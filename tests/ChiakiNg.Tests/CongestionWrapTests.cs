using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP715: what the console is actually told in the window where the sequence space wraps.
///
/// PP714 reproduced the C's span arithmetic: two uint16 promote to int before the subtraction, so a
/// ceiling numerically below its floor is a NEGATIVE int widened into a uint64 - about 1.8e19,
/// rather than the small positive difference the C's own comment claims. That happens once per wrap
/// of the sequence space, on a stream that lost nothing.
///
/// PP715 ASKED WHAT THE CONSOLE DOES WITH IT and predicted it is told the maximum loss the settings
/// allow. IT IS NOT. The clamp does aim there, and then both numbers are narrowed to sixteen bits
/// on the way out - and the low sixteen bits of a pair that was computed against a total near 2^64
/// have no relation to the ratio the clamp produced. The console is told about three quarters loss,
/// from a window in which every packet arrived.
///
/// So the answer is worse than the prediction and in a different way: the clamp is not merely
/// firing on a healthy stream, it is being DEFEATED on that stream by the send's own narrowing.
/// </summary>
public class CongestionWrapTests(ITestOutputHelper output)
{
    /// <summary>How many sequence numbers there are, which is how often this window comes round.</summary>
    public const int SequenceSpace = 65536;

    /// <summary>
    /// Walk the ceiling up to <paramref name="target"/> in steps the serial order accepts.
    ///
    /// It cannot be jumped to. Under RFC 1982 a number more than half the space ahead is NOT
    /// greater, so pushing 65500 into a fresh object leaves the ceiling at zero - which is how the
    /// first version of this test built a window it thought was a wrap and was not. A real stream
    /// arrives at 65500 by counting, and so does this.
    /// </summary>
    private static void WalkCeilingTo(ManagedPacketStats stats, ushort target)
    {
        for (var at = 30000; at < target; at += 30000)
            stats.PushSeq((ushort)at);

        stats.PushSeq(target);
    }

    /// <summary>
    /// One window of audio that crosses 65535 with nothing lost, driven through the real objects.
    /// </summary>
    private static PacketWindow WrappingWindow()
    {
        var stats = new ManagedPacketStats();

        // Close a window at 65500, which leaves the floor there.
        WalkCeilingTo(stats, 65500);
        stats.Read(reset: true);

        // 65501..65535 and then 0..5 - forty-one packets, every one of them arriving, and the
        // ceiling ends BELOW the floor because a wrapped number is greater under RFC 1982.
        for (var seq = 65501; seq <= 65535; seq++)
            stats.PushSeq((ushort)seq);

        for (ushort seq = 0; seq <= 5; seq++)
            stats.PushSeq(seq);

        return stats.Read(reset: true);
    }

    /// <summary>An ordinary window: two hundred numbers advanced, two hundred packets arrived.</summary>
    private static PacketWindow OrdinaryWindow()
    {
        var stats = new ManagedPacketStats();

        stats.PushSeq(1000);
        stats.Read(reset: true);

        for (var seq = 1001; seq <= 1200; seq++)
            stats.PushSeq((ushort)seq);

        return stats.Read(reset: true);
    }

    /// <summary>
    /// THE WINDOW ITSELF: forty-one packets arrive and the span reads as almost 2^64.
    ///
    /// Driven rather than constructed, so the sequence comparison, the ceiling and the floor are the
    /// port's own - a fixture with the number typed in would assert nothing about how it arises.
    /// </summary>
    [Fact]
    public void AWindowThatCrossesTheWrapReportsAnEnormousSpan()
    {
        PacketWindow window = WrappingWindow();

        output.WriteLine($"received {window.Received}, lost {window.Lost}");

        Assert.Equal(41ul, window.Received);
        Assert.Equal(18446744073709486080ul, window.Lost);
    }

    /// <summary>And the ordinary window beside it loses nothing at all.</summary>
    [Fact]
    public void AnOrdinaryWindowLosesNothing()
    {
        PacketWindow window = OrdinaryWindow();

        Assert.Equal(200ul, window.Received);
        Assert.Equal(0ul, window.Lost);
    }

    /// <summary>
    /// THE ANSWER PP715 ASKED FOR: the console is told three quarters, not the clamp's five percent.
    ///
    /// The clamp aims at the ceiling, correctly, against a total near 2^64. Then both numbers are
    /// narrowed to sixteen bits, and what survives is their low half - which has no relation to the
    /// ratio between them. The prediction in the filing was that the console sees the maximum
    /// allowed loss; it sees about fifteen times that.
    /// </summary>
    [Fact]
    public void TheConsoleIsToldThreeQuartersAndNotTheCeiling()
    {
        PacketWindow window = WrappingWindow();

        (ulong wideReceived, ulong wideLost) =
            ManagedCongestionControl.ClampWide(window.Received, window.Lost, 0.05, out double measured);

        CongestionReport sent = ManagedCongestionControl.Clamp(window.Received, window.Lost, 0.05, out _);

        output.WriteLine($"measured {measured}");
        output.WriteLine($"clamp produced {wideReceived}/{wideLost}");
        output.WriteLine($"console told   {sent.Received}/{sent.Lost} = {sent.Loss:P2}");

        // The measurement is total loss, over a window that lost nothing.
        Assert.Equal(1.0, measured, 6);

        // The clamp did its job at full width.
        Assert.Equal(0.05, (double)wideLost / (wideReceived + wideLost), 6);

        // And the narrowing threw that away.
        Assert.Equal(16425, sent.Received);
        Assert.Equal(49152, sent.Lost);
        Assert.Equal(0.7495, sent.Loss, 4);

        Assert.True(ManagedCongestionControl.NarrowingChangedTheReport(window.Received, window.Lost, 0.05));
    }

    /// <summary>
    /// On an ordinary window the narrowing changes nothing, which is why this was never noticed.
    ///
    /// The pair is small, so its low sixteen bits are the pair. Every window that is not a wrap
    /// behaves, and one in sixty-five thousand does not.
    /// </summary>
    [Fact]
    public void AnOrdinaryWindowSurvivesTheNarrowing()
    {
        PacketWindow window = OrdinaryWindow();

        CongestionReport sent = ManagedCongestionControl.Clamp(window.Received, window.Lost, 0.05, out double measured);

        Assert.Equal(0.0, measured, 6);
        Assert.Equal(new CongestionReport(200, 0), sent);
        Assert.Equal(0.0, sent.Loss, 6);

        Assert.False(ManagedCongestionControl.NarrowingChangedTheReport(window.Received, window.Lost, 0.05));
    }

    /// <summary>
    /// The ceiling cannot be jumped to, which is why the window is once per space and not per push.
    ///
    /// A number more than half the sequence space ahead is not greater under RFC 1982, so it does
    /// not move the ceiling at all. That is the rule that makes a wrap reachable only by counting
    /// up to it - and the rule the first version of this file got wrong, building a window it
    /// believed was a wrap out of a ceiling that had never left zero.
    /// </summary>
    [Fact]
    public void TheCeilingOnlyMovesForwardByLessThanHalfTheSpace()
    {
        Assert.Equal(SequenceSpace, 1 << 16);

        var jumped = new ManagedPacketStats();
        jumped.PushSeq(65500);

        // One packet arrived and the ceiling never moved, so the span is zero and nothing is lost.
        Assert.Equal(new PacketWindow(1, 0), jumped.Read(reset: false));

        var walked = new ManagedPacketStats();
        WalkCeilingTo(walked, 65500);

        // Walking there moves it, and the span is the distance covered.
        PacketWindow window = walked.Read(reset: false);

        output.WriteLine($"walked: received {window.Received}, lost {window.Lost}");

        Assert.Equal(65500ul - window.Received, window.Lost);
    }
}
