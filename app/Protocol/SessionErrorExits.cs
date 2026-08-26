using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP339: the session thread's two exit macros mean different things, and one stood where the
/// other was meant.
///
/// QUIT unlocks the state mutex and jumps. CHECK_STOP jumps ONLY where should_stop is set - it is
/// a cancellation poll, and on a session nobody asked to stop it returns and execution carries on
/// past it. They read almost the same at a call site, and a failure written with the wrong one
/// looks exactly like a failure written with the right one.
///
/// The rudp init had CHECK_STOP. It logged at ERROR level and then fell through with rudp NULL, so
/// the PSN registration block was skipped and the thread made an ordinary session request - which
/// walks connect_info.host_addrinfos, a field chiaki_session_init fills only in the branch for
/// sessions that have NO holepunch session. It was NULL, the loop ran zero times, and the session
/// ended reporting that no address answered. The one failure that had been diagnosed became one
/// that had not.
///
/// WHAT IS CHECKED HERE IS THE SHAPE AND NOT THE LINE. A failure path that logs at ERROR level and
/// then reaches the statement after it is the defect, wherever it is written - so this reads every
/// such block in the thread rather than the one that was wrong.
/// </summary>
public static class SessionErrorExits
{
    /// <summary>Where the thread lives.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The macro that ends the session whatever the state.</summary>
    public const string ErrorExit = "QUIT(";

    /// <summary>The macro that ends it only where a stop was asked for.</summary>
    public const string CancellationPoll = "CHECK_STOP(";

    /// <summary>
    /// Every place the thread logs an error and then polls for cancellation instead of exiting.
    ///
    /// A CHECK_STOP is legitimate on its own - it is how the thread notices a stop between steps.
    /// What is not is one standing as the whole body of an <c>if</c> that has just logged a
    /// failure, because then the failure has no exit at all.
    /// </summary>
    /// <returns>The logged message of each such block, so a failure names what it found.</returns>
    public static IReadOnlyList<string> ErrorsThatOnlyPollForCancellation(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        var found = new List<string>();

        for (int at = core.IndexOf("CHIAKI_LOGE(", StringComparison.Ordinal);
             at >= 0;
             at = core.IndexOf("CHIAKI_LOGE(", at + 1, StringComparison.Ordinal))
        {
            int lineEnd = core.IndexOf('\n', at);
            if (lineEnd < 0)
                break;

            // What the next statement is. A block that exits names QUIT somewhere before it closes;
            // one that only polls names CHECK_STOP and then meets the closing brace.
            int close = core.IndexOf('}', lineEnd);
            if (close < 0)
                break;

            string rest = core[lineEnd..close];

            if (rest.Contains(CancellationPoll, StringComparison.Ordinal)
                && !rest.Contains(ErrorExit, StringComparison.Ordinal))
            {
                found.Add(core[at..lineEnd].Trim());
            }
        }

        return found;
    }
}
