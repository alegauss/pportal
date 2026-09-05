using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP714: packetstats.c against its port, over the arithmetic congestion control divides by.
///
/// The oracle drives the C end to end - both kinds of push, a read, MORE pushes, a second read -
/// because the reset is only visible across two windows, and a single read would agree with a port
/// that zeroed the sequence floor instead of raising it.
///
/// Under PP707, whose host owes the thread that reads these.
/// </summary>
public class ManagedPacketStatsTests(ITestOutputHelper output)
{
    /// <summary>The same scenario against the managed stats, pushed on both sides of the read.</summary>
    private static (PacketWindow First, PacketWindow Second) Managed(
        IReadOnlyList<(ulong Received, ulong Lost)> generations,
        IReadOnlyList<ushort> before,
        IReadOnlyList<ushort> after,
        bool reset)
    {
        var stats = new ManagedPacketStats();

        foreach ((ulong received, ulong lost) in generations)
            stats.PushGeneration(received, lost);

        foreach (ushort seq in before)
            stats.PushSeq(seq);

        PacketWindow first = stats.Read(reset);

        foreach (ushort seq in after)
            stats.PushSeq(seq);

        return (first, stats.Read(false));
    }

    /// <summary>
    /// The cases, chosen for the branches rather than for coverage.
    ///
    /// A window with no sequence arm at all, a clean run, a gap, more arrivals than the span is
    /// wide, a second window opened by the reset, and the climb past 65535 that is the reason this
    /// has an oracle at all. The ceiling only advances under RFC 1982, so reaching a high sequence
    /// number takes steps of less than 32768 - which is what a real stream does and what a case
    /// that jumped straight to 65530 would silently fail to set up.
    /// </summary>
    public static IEnumerable<(string Name, (ulong, ulong)[] Gen, ushort[] Before, ushort[] After, bool Reset)> Cases()
    {
        yield return ("nothing at all", [], [], [], true);
        yield return ("generations only", [(10UL, 2UL), (7UL, 0UL)], [], [], true);
        yield return ("one packet", [], [5], [], true);
        yield return ("a clean run", [], [1, 2, 3, 4, 5], [], true);
        yield return ("a gap of three", [], [1, 2, 6], [], true);
        yield return ("reordered arrivals", [], [1, 4, 2, 3], [], true);
        yield return ("duplicates past the span", [], [1, 2, 2, 2, 3], [], true);
        yield return ("both arms", [(4UL, 1UL)], [10, 11, 14], [], true);
        yield return ("a second window", [], [1, 2, 3], [4, 5, 9], true);
        yield return ("a second window, no reset", [], [1, 2, 3], [4, 5, 9], false);
        yield return ("generations after the read", [(4UL, 1UL)], [10, 11], [12], true);
        yield return ("climbing toward the wrap", [], [30000, 60000], [], true);
        yield return ("across 65535", [], [30000, 60000], [100], true);
        yield return ("across 65535 without a reset", [], [30000, 60000], [100], false);
        yield return ("one generation, huge", [(70000UL, 3UL)], [], [], true);
    }

    /// <summary>Every case, both reads, against the C.</summary>
    [Fact]
    public void TheManagedStatsAnswerWhatTheCAnswers()
    {
        foreach ((string name, (ulong, ulong)[] gen, ushort[] before, ushort[] after, bool reset) in Cases())
        {
            (PacketWindow nativeFirst, PacketWindow nativeSecond) =
                NativePacketStats.Run(gen, before, after, reset);
            (PacketWindow mineFirst, PacketWindow mineSecond) = Managed(gen, before, after, reset);

            output.WriteLine(
                $"{name}: C {nativeFirst.Received}/{nativeFirst.Lost} then {nativeSecond.Received}/"
                + $"{nativeSecond.Lost}; mine {mineFirst.Received}/{mineFirst.Lost} then "
                + $"{mineSecond.Received}/{mineSecond.Lost}");

            Assert.Equal(nativeFirst, mineFirst);
            Assert.Equal(nativeSecond, mineSecond);
        }
    }

    /// <summary>
    /// A reset moves the floor UP rather than to zero, which the second window is what shows.
    ///
    /// The port this test exists to refuse zeroes seq_min. It would agree with the C on the first
    /// read of every case above and then report the whole run's span as the next window's loss, on
    /// a stream that lost nothing.
    /// </summary>
    [Fact]
    public void AResetMovesTheFloorToTheCeilingAndNotToZero()
    {
        // Starting at 1, because a fresh stats has a floor of ZERO and a stream that opened at 100
        // would report 99 lost in its first window before anything here was being tested.
        var stats = new ManagedPacketStats();
        foreach (ushort seq in (ushort[])[1, 2, 3, 4])
            stats.PushSeq(seq);

        PacketWindow first = stats.Read(true);
        Assert.Equal(new PacketWindow(4, 0), first);

        // Four more, contiguous. A floor at 4 spans 4 and accounts for all of them; a floor zeroed
        // spans 8 and reports 4 lost on a stream that lost nothing.
        foreach (ushort seq in (ushort[])[5, 6, 7, 8])
            stats.PushSeq(seq);

        PacketWindow second = stats.Read(false);
        output.WriteLine($"second window: {second.Received} received, {second.Lost} lost");

        Assert.Equal(new PacketWindow(4, 0), second);
    }

    /// <summary>
    /// The span is an INT subtraction widened, not a sixteen-bit wrap - which is what the C's own
    /// comment on that line calls it.
    ///
    /// Both operands promote to int before the minus, so a ceiling below its floor is a negative
    /// int converted to uint64: about 1.8e19 rather than the 101 that sixteen-bit wraparound would
    /// give. The port reproduces it because that is what the console is told today.
    ///
    /// Reaching the state takes a climb, because the ceiling only rises for a sequence number that
    /// is GREATER under RFC 1982 and 60000 is not greater than 0.
    /// </summary>
    [Fact]
    public void ACeilingBelowItsFloorReportsAnAstronomicalLoss()
    {
        var stats = new ManagedPacketStats();

        stats.PushSeq(30000);
        stats.PushSeq(60000);

        // Close the window. The floor is now 60000.
        stats.Read(true);

        // 100 is greater than 60000 under RFC 1982 - the stream crossing 65535 - so the ceiling
        // moves DOWN to 100 numerically.
        stats.PushSeq(100);
        PacketWindow window = stats.Read(false);

        output.WriteLine($"{window.Received} received, {window.Lost} lost");

        Assert.Equal(1UL, window.Received);
        Assert.True(window.Lost > ushort.MaxValue, $"{window.Lost} is a sixteen-bit wrap, not the C's");
        Assert.Equal(unchecked((ulong)(100 - 60000)) - 1, window.Lost);

        // And the C says the same, which is what makes this the behaviour rather than my arithmetic.
        (_, PacketWindow second) = NativePacketStats.Run([], [30000, 60000], [100], true);
        Assert.Equal(window, second);
    }

    /// <summary>A reordered arrival raises the count without pulling the ceiling backwards.</summary>
    [Fact]
    public void AReorderedPacketDoesNotMoveTheCeilingBack()
    {
        var stats = new ManagedPacketStats();
        foreach (ushort seq in (ushort[])[10, 13, 11, 12])
            stats.PushSeq(seq);

        // Ceiling 13, floor 0, span 13; four received. Nothing here reaches the C's odd branch.
        PacketWindow window = stats.Read(false);
        Assert.Equal(new PacketWindow(4, 9), window);
    }

    /// <summary>Reset() and Read(true) close the window the same way, as the C's two entry points do.</summary>
    [Fact]
    public void ResetAndAResettingReadLeaveTheSameFloor()
    {
        var explicitly = new ManagedPacketStats();
        var byReading = new ManagedPacketStats();

        foreach (ushort seq in (ushort[])[7, 8, 20])
        {
            explicitly.PushSeq(seq);
            byReading.PushSeq(seq);
        }

        explicitly.PushGeneration(3, 4);
        byReading.PushGeneration(3, 4);

        explicitly.Reset();
        byReading.Read(true);

        Assert.Equal(explicitly.Read(false), byReading.Read(false));
        Assert.Equal(new PacketWindow(0, 0), explicitly.Read(false));
    }

    /// <summary>Both arms are summed rather than one winning, which a single-armed port would pass.</summary>
    [Fact]
    public void TheTwoArmsAreAdded()
    {
        var stats = new ManagedPacketStats();
        stats.PushGeneration(100, 5);
        foreach (ushort seq in (ushort[])[1, 2, 3])
            stats.PushSeq(seq);

        PacketWindow window = stats.Read(false);

        // 100 + 3 received; 5 lost from the frame, and the span of 3 accounts for its 3 arrivals.
        Assert.Equal(new PacketWindow(103, 5), window);
        Assert.Equal(108UL, window.Total);
    }
}
