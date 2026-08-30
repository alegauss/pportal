using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP538: the holepunch session's stop, which is two booleans with two different disciplines.
///
/// PP537 left <c>chiaki_holepunch_main_thread_cancel</c> as the only function in holepunch.c with
/// no managed counterpart, because everything it stops is what PP533 has to build. This is that
/// stop, written ahead of the loop.
///
/// MAIN_SHOULD_STOP IS A ONE-SHOT, NOT A FLAG. The C sets it true in the cancel and CONSUMES it at
/// every checkpoint: all fourteen read it under the stop mutex and set it back to false in the same
/// critical section. So a cancel is delivered exactly once, to whichever checkpoint reaches it
/// first - and a port that made it a plain bool would deliver one cancel to every checkpoint
/// instead of one, which is a different program that passes the same casual reading.
///
/// WS_THREAD_SHOULD_STOP IS THE OPPOSITE, and sits beside it under the same mutex: set true, read
/// without consuming, so the websocket thread sees it every time round. Two booleans, one lock,
/// two lifetimes.
///
/// THE SIGNAL IS OUTSIDE THE LOCK. The C stops the select pipe inside the critical section and
/// signals the notification condition after releasing it. A managed loop that signalled while
/// holding the lock would work and would not be this, so the order is a parameter of the model
/// rather than an accident of how it happens to be written.
/// </summary>
public sealed class HolepunchStop
{
    /// <summary>
    /// The C's stop_mutex. Both booleans are read and written under it, including the initial
    /// clearing - which is why a caller cannot see one set and the other not.
    /// </summary>
    private readonly Lock gate = new();

    private bool mainShouldStop;
    private bool websocketShouldStop;

    /// <summary>
    /// The cancel: <c>chiaki_holepunch_main_thread_cancel</c>.
    /// </summary>
    /// <param name="stopWebsocketThread">
    /// The C's <c>stop_thread</c>. True also stops the select pipe; false only logs, and either way
    /// the main loop is asked to stop.
    /// </param>
    /// <param name="stopSelectPipe">
    /// What the C does inside the critical section. Injected because a select pipe is an OS handle
    /// this type has no business owning, and because whether it happens under the lock is part of
    /// what is being modelled.
    /// </param>
    /// <param name="signal">
    /// The notification condition, signalled AFTER the lock is released. Injected for the same
    /// reason and asserted in the same place.
    /// </param>
    public void Cancel(bool stopWebsocketThread, Action? stopSelectPipe = null, Action? signal = null)
    {
        lock (gate)
        {
            if (stopWebsocketThread)
            {
                websocketShouldStop = true;
                stopSelectPipe?.Invoke();
            }

            mainShouldStop = true;
        }

        // Outside, deliberately: see the note on the type.
        signal?.Invoke();
    }

    /// <summary>
    /// One checkpoint, as all fourteen of the C's are written: true exactly once per cancel, and
    /// false for every caller after it.
    /// </summary>
    public bool CheckAndConsume()
    {
        lock (gate)
        {
            if (!mainShouldStop)
                return false;

            mainShouldStop = false;
            return true;
        }
    }

    /// <summary>
    /// The websocket thread's read, which does not consume. Answers true every time once asked to
    /// stop, which is what a loop polling it needs and what the main flag deliberately is not.
    /// </summary>
    public bool WebsocketShouldStop
    {
        get
        {
            lock (gate)
                return websocketShouldStop;
        }
    }

    /// <summary>
    /// Both cleared, under the lock, as <c>chiaki_holepunch_session_start</c> does before it runs.
    /// </summary>
    public void Reset()
    {
        lock (gate)
        {
            mainShouldStop = false;
            websocketShouldStop = false;
        }
    }

    /// <summary>Whether this thread holds the lock, so the ordering above can be asserted.</summary>
    public bool LockHeld => gate.IsHeldByCurrentThread;
}

/// <summary>
/// PP538: the C's side of the same thing, read from holepunch.c so the model above cannot drift
/// from it silently.
/// </summary>
public static partial class HolepunchStopSource
{
    /// <summary>Where the discipline lives.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>How many places test the main stop flag.</summary>
    public static int Checkpoints(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Count(source, "if(session->main_should_stop)");
    }

    /// <summary>
    /// How many of them consume it. Equal to <see cref="Checkpoints"/> is the claim: a checkpoint
    /// that read without clearing would make the cancel reach every later one too.
    /// </summary>
    public static int ConsumingCheckpoints(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // The clear has to be the FIRST statement of the block the test opens, which is what the
        // shape below says and what a window of N characters does not: a fixed width counted 13 of
        // the 14 here, because one site's indentation is wider than the others and the guess was
        // tuned to the rest. Structure, not distance.
        return ConsumeRegex().Matches(source.ReplaceLineEndings("\n")).Count;
    }

    /// <summary>One function that honours a cancel, and how many times it checks.</summary>
    /// <param name="Function">The C function.</param>
    /// <param name="Checks">How many checkpoints it holds.</param>
    /// <param name="IsWait">Whether it is one of the three blocking waits.</param>
    public sealed record CancelPoint(string Function, int Checks, bool IsWait);

    /// <summary>
    /// PP539: which functions honour a cancel, and how often.
    ///
    /// Attribution is by walking BACK from the checkpoint to the nearest line that begins in
    /// column zero, contains an open parenthesis and does not end in a semicolon - which is a
    /// definition and not a declaration. Two earlier attempts walked function boundaries forwards
    /// with a regex that misses a multi-line signature, and both put three checkpoints inside
    /// session_message_get_payload, which does not check cancellation at all. The direction is what
    /// fixed it: from the checkpoint, the previous definition is unambiguous.
    /// </summary>
    public static IReadOnlyList<CancelPoint> CancelPoints(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("if(session->main_should_stop)", StringComparison.Ordinal))
                continue;

            if (EnclosingFunction(lines, i) is not { } function)
                continue;

            if (!counts.TryGetValue(function, out int seen))
                order.Add(function);

            counts[function] = seen + 1;
        }

        return [.. order.Select(f => new CancelPoint(f, counts[f], Waits.Contains(f, StringComparer.Ordinal)))];
    }

    /// <summary>
    /// The three blocking waits, which are the half of the answer a reader would not guess: a
    /// cancel arriving mid-wait does not wait out the timeout, because each of these checks the
    /// one-shot itself and answers CHIAKI_ERR_CANCELED.
    /// </summary>
    public static IReadOnlyList<string> Waits { get; } =
        ["wait_for_notification", "wait_for_session_message", "wait_for_session_message_ack"];

    private static string? EnclosingFunction(string[] lines, int from)
    {
        for (int j = from - 1; j >= 0; j--)
        {
            string line = lines[j];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || !char.IsLetter(line[0]))
                continue;
            if (!line.Contains('(') || line.TrimEnd().EndsWith(';'))
                continue;

            int open = line.IndexOf('(');
            string head = line[..open];
            int space = head.LastIndexOfAny([' ', '*', '\t']);
            return head[(space + 1)..].Trim();
        }

        return null;
    }

    /// <summary>Plain reads of the websocket flag, which do not consume.</summary>
    public static int WebsocketReads(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Count(source, "bool should_stop = session->ws_thread_should_stop;");
    }

    /// <summary>A checkpoint whose block opens by clearing the flag.</summary>
    [GeneratedRegex(
        @"if\(session->main_should_stop\)\s*\n\s*\{\s*\n\s*session->main_should_stop = false;",
        RegexOptions.None, matchTimeoutMilliseconds: 10000)]
    private static partial Regex ConsumeRegex();

    private static int Count(string source, string needle)
    {
        var found = 0;
        var at = 0;

        while ((at = source.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            found++;
            at += needle.Length;
        }

        return found;
    }
}
