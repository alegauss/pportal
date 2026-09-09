using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>
/// PP835: whether the run reaches its draw loop without waiting for a controller.
///
/// A USER REPORTED IT AS A FREEZE and two live runs held it. StreamRun opened the pad and only then
/// entered the loop that presents frames, and <see cref="PadFeed.Start"/> is allowed ten seconds for
/// the SDL thread plus five to open a controller on it. A pad already plugged in makes that instant,
/// which is why every run before it looked fine; one connected while the session is coming up does
/// not, and across that window nothing is drawn while the receiver keeps decoding on its own thread.
///
/// READ AS A HAZARD AND NOT AS A FIX. The check is not that some particular helper is called - that
/// is a name a rename empties - but that the blocking open does not sit on the path to the draw. Any
/// arrangement that keeps it off that thread passes; the one arrangement that cost a session does
/// not.
///
/// AND THE OPEN ITSELF IS NOT THE SUBJECT. Fifteen seconds is a reasonable bound for a device that
/// may not be there. What was wrong is who waited.
/// </summary>
public static class PadOpeningSource
{
    /// <summary>The run whose order this is about.</summary>
    public const string RunRelativePath = @"app\Session\StreamRun.cs";

    /// <summary>The blocking open, spelled with its receiver so the declaration is not a call.</summary>
    public const string TheBlockingOpen = ".Start()";

    /// <summary>What the pad is constructed as, which is where the path to the draw begins.</summary>
    public const string ThePad = "new PadFeed(";

    /// <summary>And the call that presents frames, which is what must not be behind it.</summary>
    public const string TheDraw = "Draw(";

    /// <summary>The one construct that takes work off the calling thread here.</summary>
    public const string OffThisThread = "Task.Run(";

    /// <summary>
    /// Whether the run opens its pad off the thread that draws.
    ///
    /// AFFIRMATIVE AND NOT A HAZARD REPORT, which is the convention every drift check in this tree
    /// keeps: handed nothing, a reader says NO. A method answering "no hazard found" would say yes
    /// to an empty file and pass on a machine that could not read the source at all - which is the
    /// one failure a check like this must not have.
    ///
    /// So all three have to be there: a pad is constructed, a draw follows it, and every open
    /// between them is inside something that does not run on this thread.
    /// </summary>
    public static bool TheRunOpensThePadOffTheDrawingThread(string runSource)
    {
        ArgumentNullException.ThrowIfNull(runSource);

        string code = DeadAssertions.CodeOnly(runSource);

        int pad = code.IndexOf(ThePad, StringComparison.Ordinal);
        if (pad < 0)
            return false;

        int draw = code.IndexOf(TheDraw, pad, StringComparison.Ordinal);
        if (draw < 0)
            return false;

        string between = code[pad..draw];
        var opens = 0;

        // Every open between the pad and the draw has to be inside something that does not run on
        // this thread. One that is not is the order the freeze was.
        foreach (int open in Occurrences(between, TheBlockingOpen))
        {
            if (!InsideAnOffThreadCall(between, open))
                return false;

            opens++;
        }

        return opens > 0;
    }

    /// <summary>StreamRun.cs, or null outside a checkout.</summary>
    public static string? LocateRun() => SanitizerSource.LocateRelative(RunRelativePath);

    /// <summary>Every index at which a marker occurs.</summary>
    private static IEnumerable<int> Occurrences(string code, string marker)
    {
        for (int at = code.IndexOf(marker, StringComparison.Ordinal);
            at >= 0;
            at = code.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            yield return at;
        }
    }

    /// <summary>
    /// Whether an offset sits inside the parentheses of an <see cref="OffThisThread"/> call.
    ///
    /// Counted rather than matched: the call's argument is a lambda with its own parentheses and
    /// braces, so the only reliable reading is whether the depth opened by that call has closed
    /// again before the offset is reached.
    /// </summary>
    private static bool InsideAnOffThreadCall(string code, int at)
    {
        foreach (int call in Occurrences(code, OffThisThread))
        {
            if (call > at)
                break;

            int from = call + OffThisThread.Length;
            var depth = 1;
            int scan = from;

            for (; scan < code.Length && depth > 0; scan++)
            {
                if (code[scan] == '(')
                    depth++;
                else if (code[scan] == ')')
                    depth--;
            }

            // Unbalanced to the end of the file counts as enclosing: the alternative is reporting a
            // hazard because a reader ran out of text.
            if (at >= from && (depth > 0 || at < scan))
                return true;
        }

        return false;
    }
}
