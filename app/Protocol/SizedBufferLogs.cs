using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP405: a log that prints a sized buffer with the conversion that has no size.
///
/// <c>%s</c> stops at the first zero byte. A receive buffer has no zero byte at the length the
/// caller was handed - it has whatever arrived next - so the two are only compatible when the buffer
/// is a C string, and neither of these was.
///
/// THE CTRL ONE IS THE WORSE OF THE TWO. The read loop frames messages out of one long-lived buffer,
/// handing each handler a pointer eight bytes in and a size, then memmoving the remainder forward.
/// The session id handler prints that pointer on the branch where the payload is under two bytes -
/// so what a short payload writes to the log is the control messages queued behind it, on a channel
/// that carries the session id and the login PIN exchange.
///
/// PP352 DOES NOT CATCH IT, and the reason is worth keeping. Its rule is that no handler reads a
/// payload byte without first looking at the size, and this handler does look: it tests
/// <c>payload_size &lt; 2</c> and then prints the payload anyway. The check and the defect sit on the
/// same line of reasoning, and only one of them is about the read.
///
/// THE RULE IS OVER THE TREE, not over the two that were found. Both were one conversion apart from
/// correct - <c>%.*s</c> with the length already in hand - and the next one written will be too.
/// </summary>
public static partial class SizedBufferLogs
{
    /// <summary>The tree this reads.</summary>
    public const string RelativePath = @"lib\src";

    /// <summary>That directory, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateDirectory(RelativePath);

    /// <summary>The names that mean a sized buffer rather than a string, in this tree.</summary>
    public static IReadOnlyList<string> BufferNames { get; } = ["payload", "buf", "data"];

    /// <summary>Every log in the tree that prints one of those with <c>%s</c>.</summary>
    /// <returns>File name to the log text, so a failure names what it found.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Offenders(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(directory, "*.c", SearchOption.AllDirectories))
        {
            IReadOnlyList<string> logs = InFile(File.ReadAllText(path));
            if (logs.Count > 0)
                found[Path.GetFileName(path)] = logs;
        }

        return found;
    }

    /// <summary>The same, for one file's text.</summary>
    public static IReadOnlyList<string> InFile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Through Code, and not as a convenience. Both sites were corrected with a note above them
        // saying "%.*s and not %s", and a reader that saw comments would count the correction as
        // the defect - which PP399, PP400 and PP401 each did once before PP403 made this the habit.
        return [.. UnsizedLog().Matches(CCall.Code(source)).Select(m => m.Value.Trim())];
    }

    /// <summary>
    /// Whether every log in a file that prints one of these buffers says how long it is.
    ///
    /// PP272: a file with no logs in it answers NO. Read the other way this is a bare absence, and
    /// a bare absence is satisfied by the empty string.
    /// </summary>
    public static bool EveryLogSaysHowLong(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return code.Contains("CHIAKI_LOG", StringComparison.Ordinal) && InFile(code).Count == 0;
    }

    /// <summary>
    /// PP576: the pattern, named so the names inside it can be compared with
    /// <see cref="BufferNames"/>.
    ///
    /// The two were separate copies of one list - the property said "payload", "buf", "data" and
    /// nothing read it, while the alternation below said the same three and did all the work. They
    /// agreed, which is what copies do until one is edited: a fourth name added to the property
    /// would have changed no behaviour and failed no test.
    ///
    /// A GeneratedRegex pattern is a compile-time constant, so it cannot be built from the list.
    /// What it can be is readable, which is enough to assert that every name the list carries is a
    /// name this searches for.
    /// </summary>
    public const string UnsizedLogPattern =
        @"CHIAKI_LOG[A-Z]+\s*\([^;]*%s[^;]*,\s*(?:payload|buf|data)\s*\)\s*;";

    // A log whose last argument is one of the buffer names, with %s somewhere in the format. %.*s
    // does not match: the conversion is four characters and none of its pairs is "%s".
    [GeneratedRegex(UnsizedLogPattern)]
    private static partial Regex UnsizedLog();

    /// <summary>
    /// Which names the list carries that the pattern does not look for.
    ///
    /// One direction only, and deliberately. A pattern that searches for a name the list has not
    /// caught up with is wider than the record, which finds more and claims nothing false. A name in
    /// the LIST that the pattern ignores is the one that reads as covered and is not.
    /// </summary>
    public static IReadOnlyList<string> NamesThePatternMisses()
        => [.. BufferNames.Where(name => !UnsizedLogPattern.Contains(name, StringComparison.Ordinal))];
}
