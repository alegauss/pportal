using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP541: the wait in <c>chiaki_holepunch_session_create</c> that a failed websocket never ends.
///
/// The caller waits for the push-notification websocket with no timeout and one exit:
///
///     while (!(session->state &amp; SESSION_STATE_WS_OPEN))
///         chiaki_cond_wait(&amp;session->state_cond, &amp;session->state_mutex);
///
/// Exactly one line sets that bit, on <c>websocket_thread_func</c>'s success path. Every failure
/// the thread has skips it - a null from curl_easy_init returns straight out, and a connect that
/// fails goes to a cleanup which frees the handle, clears <c>ws_open</c> and returns without
/// touching the bit or the condition. So the caller waits forever.
///
/// AND THE CANCEL DOES NOT BREAK IT. chiaki_holepunch_main_thread_cancel signals both conds, so
/// the wait wakes - and re-tests a bit that is still clear, and waits again. PP539 found three
/// waits that check the one-shot themselves; this is a fourth that does not.
///
/// NOT PATCHED, for the reason PP107 records about its own two: every drift check in this port
/// asserts the managed side matches lib/, and a local repair would leave them agreeing with a
/// libchiaki nobody else runs. What holds it is these five facts, read from the file - so a repair
/// upstream turns this red rather than passing unnoticed, and PP533's managed loop has the
/// behaviour written down as one it must not reproduce.
/// </summary>
public static class HolepunchWebsocketWait
{
    /// <summary>Where the wait and the thread live.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The loop's test, which is the whole of its exit condition.</summary>
    public const string WaitCondition = "while (!(session->state & SESSION_STATE_WS_OPEN))";

    /// <summary>The untimed wait inside it.</summary>
    public const string UntimedWait = "chiaki_cond_wait(&session->state_cond, &session->state_mutex)";

    /// <summary>The one line that lets the loop finish.</summary>
    public const string SetsTheBit = "session->state |= SESSION_STATE_WS_OPEN;";

    /// <summary>What the thread does instead, on every failure it has.</summary>
    public const string CleanupClearsOnly = "session->ws_open = false;";

    /// <summary>
    /// The thread whose cleanup that is, which is what the search anchors on.
    ///
    /// WITH THE BRACE, so this is the definition and not the forward declaration above it. Without
    /// it the anchor landed on the declaration near the top of the file and the first cleanup label
    /// after that belongs to an entirely different function - which reported the defect repaired.
    /// The same declaration-for-definition mistake PP539's attribution had to fix.
    /// </summary>
    public const string ThreadFunction = "websocket_thread_func(void *user) {";

    /// <summary>Whether the caller still waits on the untimed condition.</summary>
    public static bool WaitIsUntimed(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int at = source.IndexOf(WaitCondition, StringComparison.Ordinal);
        if (at < 0)
            return false;

        // The wait is the next statement, so a window of a few lines is enough - and a timed wait
        // appearing there is exactly the repair this is watching for.
        string block = source.Substring(at, Math.Min(source.Length - at, 240));
        return block.Contains(UntimedWait, StringComparison.Ordinal)
            && !block.Contains("chiaki_cond_timedwait(&session->state_cond", StringComparison.Ordinal);
    }

    /// <summary>How many places set the bit the wait tests. One is the finding.</summary>
    public static int SitesSettingTheBit(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = 0;
        var at = 0;
        while ((at = source.IndexOf(SetsTheBit, at, StringComparison.Ordinal)) >= 0)
        {
            found++;
            at += SetsTheBit.Length;
        }

        return found;
    }

    /// <summary>
    /// Whether the websocket thread's cleanup leaves the waiter with nothing: it must not set the
    /// bit and must not signal the condition. True is the defect, and true is what this asserts -
    /// a repair makes it false.
    /// </summary>
    public static bool CleanupLeavesTheWaiterStuck(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Anchored on the THREAD, not on the clear: chiaki_holepunch_session_init clears ws_open
        // too, and it is earlier in the file - reading from there ran into the init's own
        // chiaki_cond_init(&session->state_cond) and reported the defect repaired.
        int thread = source.IndexOf(ThreadFunction, StringComparison.Ordinal);
        if (thread < 0)
            return false;

        int at = source.IndexOf("\ncleanup:", thread, StringComparison.Ordinal);
        if (at < 0)
            return false;

        // From the label to the end of that function, which is the return just below it.
        int end = source.IndexOf("\n}", at, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string tail = source[at..end];
        if (!tail.Contains(CleanupClearsOnly, StringComparison.Ordinal))
            return false;

        return !tail.Contains("SESSION_STATE_WS_OPEN", StringComparison.Ordinal)
            && !tail.Contains("state_cond", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the cancel signals both conditions. It does, which is why the wait wakes at all -
    /// and why waking is not the same as leaving.
    /// </summary>
    public static bool CancelSignalsBothConds(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int at = source.IndexOf("chiaki_holepunch_main_thread_cancel(Session *session", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = source.IndexOf("\n}", at, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string body = source[at..end];
        return body.Contains("chiaki_cond_signal(&session->notif_cond);", StringComparison.Ordinal)
            && body.Contains("chiaki_cond_signal(&session->state_cond);", StringComparison.Ordinal);
    }
}
