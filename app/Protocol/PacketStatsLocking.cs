using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the managed side does about the mutex the C's function takes, or does not.</summary>
public enum PortLocking
{
    /// <summary>It locks where the C locks, which is most of them.</summary>
    Same,

    /// <summary>It locks where the C does NOT. The departure this list exists for.</summary>
    LocksMore,

    /// <summary>
    /// There is no counterpart to compare: a constructor cannot race anything.
    ///
    /// The C's init takes the mutex it has just created, around fields nothing else can reach yet.
    /// A managed constructor has no such moment, so this row is neither the same nor a departure -
    /// and says so rather than being left out, which is PP713's lesson one census over.
    /// </summary>
    Construction,
}

/// <summary>One writer of the packet stats, and what each side does about the mutex.</summary>
/// <param name="Function">The C function, as packetstats.c exports it.</param>
/// <param name="CLocks">Whether its body takes the stats mutex.</param>
/// <param name="Port">What this port's counterpart does.</param>
/// <param name="Note">Why the row is what it is.</param>
public readonly record struct PacketStatsWriter(
    string Function, bool CLocks, PortLocking Port, string Note);

/// <summary>
/// PP716: the one push that does not lock, and the port's decision about it - as a value.
///
/// chiaki_packet_stats has a mutex and three of its four session-time writers take it. The reset,
/// the generation push and the get all lock; chiaki_packet_stats_push_seq does not. It increments
/// seq_received and conditionally raises seq_max with nothing held.
///
/// BOTH SIDES OF THE RACE EXIST IN A RUNNING SESSION. audioreceiver.c pushes a frame index for
/// every packet it handles, on the takion thread; PP714's congestion thread reads the same two
/// fields under the mutex every 200ms. So a read can see the count raised and the ceiling not, or a
/// reset can move seq_min while an increment is in flight and lose it. Neither corrupts memory, and
/// both produce a report wrong by a little - the kind of wrong nothing notices.
///
/// PP714'S PORT LOCKS ALL FOUR, WHICH IS A DEPARTURE, and this is where it is recorded rather than
/// left in a comment. The two precedents point opposite ways and the difference between them is
/// what decided this one: PP402 reproduced a flaw because skipping it would show the user a console
/// list the Qt client never shows - the flaw was VISIBLE - while PP499 corrected a bound that
/// underflowed, because nothing outside the process could tell. This is PP499's kind. The console
/// cannot distinguish a report off by one packet from ordinary jitter, and an unsynchronised field
/// read across managed threads is worse than the C's: it has no barrier at all.
///
/// So the lock stays, the difference is a row here, and the check below fails the day upstream
/// takes the mutex - which is the day this row should go.
/// </summary>
public static class PacketStatsLocking
{
    /// <summary>Where the mutex is.</summary>
    public const string RelativePath = @"lib\src\packetstats.c";

    /// <summary>What every writer's body takes, spelled as the C spells it.</summary>
    public const string TheLock = "chiaki_mutex_lock(&stats->mutex)";

    /// <summary>Every function packetstats.c exports that touches the fields, and what each does.</summary>
    public static IReadOnlyList<PacketStatsWriter> Writers { get; } =
    [
        new(
            "chiaki_packet_stats_init",
            true,
            PortLocking.Construction,
            "The C locks a mutex it has just made, around fields nothing else can reach."),
        new(
            "chiaki_packet_stats_reset",
            true,
            PortLocking.Same,
            "ManagedPacketStats.Reset, which moves the sequence floor rather than zeroing it."),
        new(
            "chiaki_packet_stats_push_generation",
            true,
            PortLocking.Same,
            "PushGeneration, on the video path - one call per frame."),
        new(
            "chiaki_packet_stats_push_seq",
            false,
            PortLocking.LocksMore,
            "PushSeq, on the audio path - one call per packet, and the only writer the C leaves open."),
        new(
            "chiaki_packet_stats_get",
            true,
            PortLocking.Same,
            "Read, which the 200ms congestion thread calls and is the other side of the race."),
    ];

    /// <summary>The departures, which is the answer this list exists to give.</summary>
    public static IReadOnlyList<string> Departures { get; } =
        [.. Writers.Where(one => one.Port == PortLocking.LocksMore).Select(one => one.Function)];

    /// <summary>The two threads that reach the unlocked writer and the locked reader.</summary>
    public static IReadOnlyList<string> RacingCallers { get; } =
        [@"lib\src\audioreceiver.c", @"lib\src\congestioncontrol.c"];

    /// <summary>packetstats.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Which of the exported writers the C leaves without the mutex, read out of the file.
    ///
    /// Derived rather than restated: the row above says push_seq is the one, and this is what says
    /// so independently. The day upstream locks it, the two disagree and the departure is over.
    /// </summary>
    public static IReadOnlyList<string> UnlockedIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new List<string>();

        foreach (PacketStatsWriter writer in Writers)
        {
            string? body = CFunction.Body(source, writer.Function + "(");

            if (body is not null && !body.Contains(TheLock, StringComparison.Ordinal))
                found.Add(writer.Function);
        }

        return found;
    }

    /// <summary>
    /// Whether the unlocked writer still writes both fields it is unsynchronised over.
    ///
    /// A count and a ceiling. If it ever wrote only one, the race would be narrower than this row
    /// describes - and the row is what somebody reads to decide whether the lock is still earned.
    /// </summary>
    public static bool ThePushStillWritesBothFields(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "chiaki_packet_stats_push_seq(");

        return body is not null
            && body.Contains("stats->seq_received++", StringComparison.Ordinal)
            && body.Contains("stats->seq_max = seq_num", StringComparison.Ordinal);
    }
}
