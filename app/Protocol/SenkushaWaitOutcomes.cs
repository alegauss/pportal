using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Why one of senkusha's waits came back.</summary>
public enum SenkushaWake
{
    /// <summary>The thing it was waiting for arrived.</summary>
    Finished,

    /// <summary>Nothing arrived in time.</summary>
    TimedOut,

    /// <summary>Somebody asked senkusha to stop.</summary>
    Stopped,

    /// <summary>
    /// The wait returned success, the predicate is false, and nobody asked to stop.
    ///
    /// The fourth case, and the whole of PP380. Unreachable while the predicate reads two fields -
    /// success proves one of them is set, and if it is not state_finished then the stop arm answers
    /// first. PP365's remedy adds state_failed to that predicate, and this becomes the case a
    /// handler that failed arrives in.
    /// </summary>
    NothingArrived,
}

/// <summary>What a wait's site does with that.</summary>
public enum SenkushaVerdict
{
    /// <summary>Take the measurement: the thing arrived.</summary>
    Measured,

    /// <summary>Spend this attempt and try again.</summary>
    Retry,

    /// <summary>Answer CANCELED and leave.</summary>
    Cancel,

    /// <summary>Answer a failure and leave.</summary>
    Fail,
}

/// <summary>The waits in senkusha.c, by what each is waiting for.</summary>
public enum SenkushaWaitSite
{
    /// <summary>Takion coming up.</summary>
    Connect,

    /// <summary>The protocol request ack.</summary>
    ProtocolAck,

    /// <summary>The console's bang.</summary>
    Bang,

    /// <summary>A pong, inside the RTT loop. The one that always got this right.</summary>
    Pong,

    /// <summary>An MTU response, inside the in test's retry loop.</summary>
    MtuIn,

    /// <summary>An MTU pong, inside the out test's retry loop.</summary>
    MtuOut,

    /// <summary>The client MTU command from the server.</summary>
    ClientMtuCommand,

    /// <summary>A data ack, in the helper every send goes through.</summary>
    DataAck,
}

/// <summary>
/// PP380, under PP295: six waits that logged nothing arrived and then reported success.
///
/// Every wait in senkusha.c ends the same way - the predicate came back false, the error was not a
/// timeout, and no stop was asked for. The RTT loop is the one that gets it right: its block ends in
/// `continue`, so a missing pong costs that ping and nothing else. Six others did not.
///
/// THREE REPORTED A MEASUREMENT, three reported the whole run. The MTU in test and the MTU out test
/// fell through into `success = true`; the shared data-ack helper returned the CHIAKI_ERR_SUCCESS
/// still sitting in `err`. The connect, the protocol ack and the bang waits carried that same
/// success out through QUIT, so chiaki_senkusha_run answered SUCCESS having never connected - and
/// the session then took its MTU and RTT from a test that did not run.
///
/// NONE OF IT IS REACHABLE TODAY, which is why this is a fix ahead of a fix rather than a bug
/// report. The predicate is <c>state_finished || should_stop</c>, so a wait returning SUCCESS proves
/// one of the two is set, and if it is not state_finished the stop arm answers first. The branch is
/// dead by arithmetic, not by design.
///
/// PP365 IS WHAT MAKES IT WORTH DOING NOW. It found that this same predicate ignores state_failed -
/// written ten times in this file, read nowhere. The obvious remedy is to add it, and that one
/// change makes all six live at once. So this is owed BEFORE PP365's fix, not after.
/// </summary>
public static class SenkushaWaitOutcomes
{
    /// <summary>
    /// Why a wait came back, from what the C can see at that moment.
    ///
    /// The order is the C's: the predicate first, then the timeout, then the stop - which is what
    /// makes <see cref="SenkushaWake.NothingArrived"/> the leftover rather than a case anybody
    /// wrote.
    /// </summary>
    public static SenkushaWake Classify(bool stateFinished, ChiakiError wait, bool shouldStop)
    {
        if (stateFinished)
            return SenkushaWake.Finished;

        if (wait == ChiakiError.Timeout)
            return SenkushaWake.TimedOut;

        return shouldStop ? SenkushaWake.Stopped : SenkushaWake.NothingArrived;
    }

    /// <summary>
    /// Whether a site is inside a loop that can spend an attempt and try the next one.
    ///
    /// It decides the whole answer: a site with attempts left retries, and one without has to fail.
    /// </summary>
    public static bool HasAttemptsToSpend(SenkushaWaitSite site) => site switch
    {
        SenkushaWaitSite.Pong or SenkushaWaitSite.MtuIn or SenkushaWaitSite.MtuOut => true,
        _ => false,
    };

    /// <summary>
    /// What a site answers, given why its wait came back.
    ///
    /// The row that was wrong is <see cref="SenkushaWake.NothingArrived"/>, and it now answers the
    /// same as a timeout - which is what the RTT loop always did and what the other five did not.
    /// </summary>
    public static SenkushaVerdict Answer(SenkushaWaitSite site, SenkushaWake wake) => wake switch
    {
        SenkushaWake.Finished => SenkushaVerdict.Measured,
        SenkushaWake.Stopped => SenkushaVerdict.Cancel,

        // A timeout and a silence are the same thing to a caller: the thing did not arrive.
        _ => HasAttemptsToSpend(site) ? SenkushaVerdict.Retry : SenkushaVerdict.Fail,
    };

    /// <summary>
    /// Whether the fourth case can happen at all with the predicate as it stands.
    ///
    /// False, and stated rather than left implicit: it is the reason nothing changes today, and the
    /// reason a test cannot reach these branches by driving the C.
    /// </summary>
    public static bool IsReachable(bool predicateReadsStateFailed) => predicateReadsStateFailed;

    /// <summary>
    /// What the run answered before the fix, for the three that carried a success out of a failure.
    ///
    /// Kept so the regression is named rather than described: `err` held whatever the wait left,
    /// and the wait left SUCCESS.
    /// </summary>
    public static ChiakiError AnsweredAsItWas(ChiakiError wait) => wait;
}

/// <summary>
/// PP380: the rule over every wait in senkusha.c - no block that decided nothing arrived may fall
/// through to the success beneath it.
///
/// Stated over the file because six of seven had the same defect and the seventh was the model. A
/// check on the three the section first named would have left the other three exactly as they were.
/// </summary>
public static class SenkushaWaitSource
{
    /// <summary>Where the waits live.</summary>
    public const string RelativePath = @"lib\src\senkusha.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The line every one of these blocks opens with.</summary>
    public const string Guard = "if(!senkusha->state_finished)";

    /// <summary>
    /// Every <c>if(!senkusha-&gt;state_finished)</c> block in the file, as its own text.
    ///
    /// Braces are counted rather than matched on a closing line, for the reason CFunction gives:
    /// the crude version works until the first block containing one at the start of a line.
    /// </summary>
    public static IReadOnlyList<string> WaitBlocksIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var blocks = new List<string>();

        for (int at = source.IndexOf(Guard, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(Guard, at + Guard.Length, StringComparison.Ordinal))
        {
            int open = source.IndexOf('{', at);
            if (open < 0)
                break;

            var depth = 0;
            for (int scan = open; scan < source.Length; scan++)
            {
                if (source[scan] == '{')
                {
                    depth++;
                }
                else if (source[scan] == '}' && --depth == 0)
                {
                    blocks.Add(source[(open + 1)..scan]);
                    break;
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// Whether a block answers the silence rather than falling out of itself into a success.
    ///
    /// An answer is one of four things, and which one is the site's business rather than this
    /// rule's: leave the loop turn (<c>continue</c>), leave the function (<c>return</c>), leave
    /// through a label (<c>goto</c>), or write a non-success into <c>err</c> for whoever reads it
    /// below. What is NOT an answer is a log, which is what all six of these had.
    /// </summary>
    public static bool ItAnswersRatherThanFallsThrough(string block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.Trim().Length == 0)
            return false;

        // The tail is what matters: everything after the last arm that can leave on its own. A
        // `continue` inside the timeout arm says nothing about the path that skips it.
        string[] lines = [.. block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i];
            if (line.StartsWith("//", StringComparison.Ordinal) || line is "{" or "}")
                continue;

            return line.StartsWith("continue;", StringComparison.Ordinal)
                || line.StartsWith("return ", StringComparison.Ordinal)
                || line.StartsWith("goto ", StringComparison.Ordinal)
                || line.StartsWith("QUIT(", StringComparison.Ordinal)
                || (line.StartsWith("err = CHIAKI_ERR_", StringComparison.Ordinal)
                    && !line.StartsWith("err = CHIAKI_ERR_SUCCESS", StringComparison.Ordinal));
        }

        return false;
    }

    /// <summary>The blocks that still fall through, so a failure names how many and shows them.</summary>
    public static IReadOnlyList<string> BlocksThatFallThrough(string source)
        => [.. WaitBlocksIn(source).Where(b => !ItAnswersRatherThanFallsThrough(b))];

    /// <summary>
    /// Whether the predicate still reads two fields, which is why none of this is reachable.
    ///
    /// Asserted so the day it grows a third is the day these branches are known to be live - and
    /// this rule is what makes that a safe change rather than six new ways to report a success.
    /// </summary>
    public static bool ThePredicateStillReadsTwoFields(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "static bool state_finished_cond_check");
        if (body is null)
            return false;

        return body.Contains("senkusha->state_finished || senkusha->should_stop", StringComparison.Ordinal)
            && !body.Contains("state_failed", StringComparison.Ordinal);
    }
}
