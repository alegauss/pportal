using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What a signal on the state condition achieves.</summary>
public enum WakeOutcome
{
    /// <summary>The predicate holds, so the waiting thread proceeds.</summary>
    TheThreadProceeds,

    /// <summary>
    /// The predicate does not hold, so the thread re-sleeps and waits out its full timeout - having
    /// been woken deliberately by code that expected otherwise.
    /// </summary>
    TheThreadSleepsAgain,
}

/// <summary>
/// PP365: a flag written eighteen times across two files, read nowhere, and signalled anyway.
///
/// `state_failed` is written 8 times in streamconnection.c and 10 in senkusha.c. Nothing reads it in
/// either. The wait predicate in streamconnection.c watches `state_finished`, `should_stop` and
/// `remote_disconnected`; senkusha's watches `state_finished` and `should_stop`. After each wait, the
/// run function tests `should_stop` and then `state_finished` and nothing else.
///
/// AND THE FAILURE PATHS SIGNAL THE CONDITION ANYWAY, which is what makes this a defect rather than
/// dead code. The bang handler's error label sets the flag and calls `cond_signal`; so does the
/// streaminfo handler's. The waiting thread wakes, re-evaluates the predicate it was given, finds it
/// false - because the flag just set is not in it - and goes back to sleep. Somebody wrote the wake-up
/// believing it would work.
///
/// What follows is a full EXPECT_TIMEOUT_MS after the failure is already known. The C's own log line
/// is the tell: "didn't receive bang or failed to handle it" - one sentence for two things, because at
/// that point it genuinely cannot tell them apart.
///
/// THE PORT REPRODUCES IT AS DEAD. Watching the flag would end the stream at once, which is better and
/// is different behaviour; deleting it would make the log line honest but is a redesign. Either would
/// give the port a timing no message-level comparison against the C would show. So the flag stays
/// dead here, and what is asserted is that it stays dead - in both files, because two independent
/// occurrences make this the codebase's pattern rather than one file's slip.
/// </summary>
public static partial class StateFailedFlag
{
    /// <summary>The files that carry the flag.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        @"lib\src\streamconnection.c",
        @"lib\src\senkusha.c",
    ];

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Whether the stream connection's wait predicate holds.
    ///
    /// `stateFailed` is a parameter and is IGNORED, deliberately. Leaving it out of the signature
    /// would make the port look like a design where the flag does not exist; taking it and dropping it
    /// is what the C does, and is a thing a test can hold.
    /// </summary>
    public static bool StreamPredicateHolds(
        bool stateFinished, bool shouldStop, bool remoteDisconnected, bool stateFailed)
    {
        _ = stateFailed;

        return stateFinished || shouldStop || remoteDisconnected;
    }

    /// <summary>The same for senkusha, whose predicate is the shorter of the two.</summary>
    public static bool SenkushaPredicateHolds(bool stateFinished, bool shouldStop, bool stateFailed)
    {
        _ = stateFailed;

        return stateFinished || shouldStop;
    }

    /// <summary>
    /// What a signal sent by a failure path achieves, given nothing else has changed.
    ///
    /// This is the whole finding in one function: the answer is that the thread sleeps again.
    /// </summary>
    public static WakeOutcome OutcomeOfSignallingAFailure(bool predicateHolds)
        => predicateHolds ? WakeOutcome.TheThreadProceeds : WakeOutcome.TheThreadSleepsAgain;

    [GeneratedRegex(@"state_failed", RegexOptions.None)]
    private static partial Regex AnyMention();

    [GeneratedRegex(@"->state_failed\s*=", RegexOptions.None)]
    private static partial Regex AWrite();

    /// <summary>
    /// Every mention of the flag that is not a write to it - which is to say, every read.
    ///
    /// Counted as "mentions minus writes" rather than by looking for reads directly, because a read
    /// can be spelled a dozen ways and a write cannot. If the two counts ever differ, something reads
    /// it and the port's model of a dead flag has stopped being true.
    /// </summary>
    public static int ReadsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return AnyMention().Count(source) - AWrite().Count(source);
    }

    /// <summary>Writes to it, which is the count that says the sweep found the right file.</summary>
    public static int WritesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return AWrite().Count(source);
    }

    /// <summary>
    /// Whether a wait predicate still leaves the flag out.
    ///
    /// Read out of the predicate function itself rather than off the file: the flag is written all
    /// over both files, and a check over the whole text would answer a different question.
    /// </summary>
    public static bool ThePredicateStillIgnoresIt(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "static bool state_finished_cond_check(");
        if (body is null)
            return false;

        return body.Contains("state_finished", StringComparison.Ordinal)
            && body.Contains("should_stop", StringComparison.Ordinal)
            && !body.Contains("state_failed", StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the failure paths still spend a signal on it.
    ///
    /// This is the part that is a defect rather than dead code, so it is asserted rather than left
    /// implied: if the signal ever goes away, what remains is ordinary dead code and this task's
    /// reasoning - and its section - no longer describe the file.
    /// </summary>
    public static bool TheFailurePathsStillSignal(string source, params string[] handlers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handlers);

        foreach (string handler in handlers)
        {
            string? body = CFunction.Body(source, handler);
            if (body is null)
                return false;

            int set = body.IndexOf("state_failed = true;", StringComparison.Ordinal);
            if (set < 0)
                return false;

            if (!body[set..].Contains("chiaki_cond_signal(&stream_connection->state_cond);", StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
