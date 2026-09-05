using System.Collections.Concurrent;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP716: the one writer the C leaves unlocked, and PP714's decision to lock it anyway.
///
/// PP714 ported packetstats.c and took the lock on all four session-time writers. The C takes it on
/// three. That is a departure, and it was left in a comment - which is the thing this tree does not
/// do with departures, because a comment is not read by anything.
///
/// THE DECISION IS PP499'S KIND AND NOT PP402'S. PP402 reproduced a flaw because skipping it would
/// have shown the user a console list the Qt client never shows: the flaw was visible. PP499
/// corrected a bound that underflowed, because nothing outside the process could tell. A report off
/// by one packet is indistinguishable from jitter at the console, and an unsynchronised field read
/// across managed threads has no barrier at all - so the lock stays and the difference is a row.
/// </summary>
public class PacketStatsLockingTests(ITestOutputHelper output)
{
    private static string? Read()
    {
        string? path = PacketStatsLocking.Locate();

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE DEPARTURE IS ONE ROW, and it names the writer PP716 was filed about.
    ///
    /// Asserted as the set rather than as a count: a second departure appearing is a decision
    /// somebody takes, and this is where it has to be written down to be taken.
    /// </summary>
    [Fact]
    public void TheOnlyDepartureIsTheSequencePush()
    {
        output.WriteLine(string.Join(", ", PacketStatsLocking.Departures));

        Assert.Equal(["chiaki_packet_stats_push_seq"], PacketStatsLocking.Departures);
    }

    /// <summary>And every other row says the port does what the C does, or why it cannot.</summary>
    [Fact]
    public void EveryOtherRowIsTheSameOrIsConstruction()
    {
        Assert.Equal(
            3,
            PacketStatsLocking.Writers.Count(one => one.Port == PortLocking.Same));

        Assert.Single(PacketStatsLocking.Writers, one => one.Port == PortLocking.Construction);

        // And every row says why it is what it is, because a table with no reasons is a table.
        Assert.All(
            PacketStatsLocking.Writers,
            one => Assert.False(string.IsNullOrWhiteSpace(one.Note)));
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still leaves exactly that one writer open.
    ///
    /// Read out of packetstats.c rather than restated, so the day upstream takes the mutex there,
    /// this fails and the row above has stopped being true. That is the day the lock stops being a
    /// departure and this file can go.
    /// </summary>
    [Fact]
    public void TheCStillLeavesExactlyThatWriterUnlocked()
    {
        if (Read() is not { } source)
            return;

        IReadOnlyList<string> unlocked = PacketStatsLocking.UnlockedIn(source);

        output.WriteLine(unlocked.Count == 0 ? "all locked" : string.Join(", ", unlocked));

        Assert.Equal(PacketStatsLocking.Departures, unlocked);

        // PP271: a reader that found no bodies at all would satisfy the line above by finding
        // nothing unlocked, so the other side is asserted too.
        Assert.All(
            PacketStatsLocking.Writers,
            one => Assert.NotNull(CFunction.Body(source, one.Function + "(")));
    }

    /// <summary>And it still writes both of the fields the race is over.</summary>
    [Fact]
    public void TheUnlockedPushStillWritesACountAndACeiling()
    {
        if (Read() is not { } source)
            return;

        Assert.True(
            PacketStatsLocking.ThePushStillWritesBothFields(source),
            "the unlocked push no longer writes both the count and the ceiling");
    }

    /// <summary>
    /// The port really does lock it, which is the half a row could otherwise claim on its own.
    ///
    /// Driven rather than inspected: two threads push and read the same stats as hard as they can,
    /// and every window that comes back accounts for packets that were actually pushed. Under the
    /// C's shape this is the read that can see a count raised and its ceiling not.
    /// </summary>
    [Fact]
    public void TheManagedPushIsSafeToCallWhileAReadIsRunning()
    {
        var stats = new ManagedPacketStats();
        var windows = new ConcurrentQueue<PacketWindow>();
        const int pushes = 20000;

        var pushing = new Thread(() =>
        {
            for (var at = 0; at < pushes; at++)
                stats.PushSeq((ushort)at);
        });

        var reading = new Thread(() =>
        {
            for (var at = 0; at < 500; at++)
                windows.Enqueue(stats.Read(reset: true));
        });

        pushing.Start();
        reading.Start();
        pushing.Join();
        reading.Join();

        ulong received = windows.Aggregate(0ul, (sum, one) => sum + one.Received)
            + stats.Read(reset: false).Received;

        output.WriteLine($"{windows.Count} window(s), {received} received of {pushes}");

        // Every push is accounted for exactly once across the windows and the tail.
        Assert.Equal((ulong)pushes, received);
    }
}
