using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP5: sessionlog.cpp's file naming and rotation, with no QDir or QDateTime in it.
///
/// Two clients writing into one directory have to agree about the name, because the rotation is
/// driven by parsing it. They do not agree by accident: the format string is "zzzzzz", and Qt does
/// not read that as microseconds - it reads it as "zzz" twice and prints the MILLISECONDS TWICE.
/// Every file the Qt client left on this machine says so:
///
///   chiaki_session_2026-08-11_20-10-38-088088.log
///   chiaki_session_2026-08-11_21-16-40-818818.log
///
/// 088088, 818818, 402402 - three digits, repeated. A port that read the format as six digits of
/// microseconds would write names Qt's own rotation sorts to the bottom, and the deletion loop
/// stops at the first name it cannot parse rather than skipping it. The logs would then grow
/// without limit, in a directory nobody looks at, with nothing reporting it.
/// </summary>
public static partial class SessionLogFile
{
    /// <summary>KEEP_LOG_FILES_COUNT.</summary>
    public const int KeepCount = 5;

    public const string Prefix = "chiaki_session_";
    public const string Suffix = ".log";

    /// <summary>The wildcard sessionlog.cpp filters the directory with.</summary>
    public const string Wildcard = "chiaki_session_*.log";

    /// <summary>
    /// The name for a session started at <paramref name="startedAt"/>: the local time, with the
    /// millisecond written twice, which is what Qt's "zzzzzz" produces.
    /// </summary>
    public static string NameFor(DateTime startedAt)
    {
        string ms = startedAt.Millisecond.ToString("D3", CultureInfo.InvariantCulture);
        return Prefix
            + startedAt.ToString("yyyy-MM-dd_HH-mm-ss-", CultureInfo.InvariantCulture)
            + ms + ms
            + Suffix;
    }

    /// <summary>The full path, in the same log directory the Qt build writes to.</summary>
    public static string PathFor(DateTime startedAt)
        => Path.Combine(QtPaths.LogDirectory, NameFor(startedAt));

    /// <summary>
    /// The timestamp a name carries, or null where it does not carry one.
    ///
    /// The last three fractional digits are taken as the milliseconds, which is what Qt's parser
    /// does - "zzz" twice means the second one overwrites the first. On every file Qt wrote the
    /// two halves are equal, so both readings agree; they would only diverge on a name this port
    /// wrote wrong, which is the case worth being able to see.
    /// </summary>
    public static DateTime? TimestampOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        Match m = NameRegex().Match(fileName);
        if (!m.Success)
            return null;

        // "yyyy-MM-dd_HH-mm-ss-" is twenty characters and the fraction is six, so anything
        // shorter cannot be a timestamp - and slicing it would throw rather than answer null,
        // which is what a stray file in the directory would then do to the rotation.
        string stamp = m.Groups[1].Value;
        if (stamp.Length != 26)
            return null;

        if (!DateTime.TryParseExact(stamp[..^6] + stamp[^3..], "yyyy-MM-dd_HH-mm-ss-fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            return null;

        return parsed;
    }

    /// <summary>Whether both halves of the fractional part are the same, as Qt's format makes them.</summary>
    public static bool FractionIsDoubledMillisecond(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        Match m = NameRegex().Match(fileName);
        if (!m.Success)
            return false;

        string stamp = m.Groups[1].Value;
        return stamp.Length == 26
            && stamp[^6..].All(char.IsAsciiDigit)
            && stamp[^6..^3] == stamp[^3..];
    }

    /// <summary>
    /// Which of the existing names the Qt client would delete, in the order it decides.
    ///
    /// Newest first by parsed timestamp, keep five, delete the rest - and stop at the first entry
    /// whose name does not parse. That `break` reads alarming and is not: an unparseable name has
    /// no date, and a dateless entry sorts below every real one, so the loop reaches it only after
    /// every actual log has been considered. What it does mean is that a stray file matching the
    /// wildcard is never deleted, however many there are. Reproduced rather than corrected: a port
    /// that deleted more than the Qt build does is a port that removes a file somebody was about
    /// to attach to a bug report.
    /// </summary>
    public static IReadOnlyList<string> ToRemove(IEnumerable<string> existingNames, int keep = KeepCount)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        // Descending by timestamp, with an unparseable name sorting below every real one - which
        // is where an invalid QDateTime lands in Qt's own comparison.
        var ordered = existingNames
            .Select(name => (Name: name, Stamp: TimestampOf(name)))
            .OrderByDescending(e => e.Stamp ?? DateTime.MinValue)
            .ToList();

        var removed = new List<string>();
        for (int i = keep; i < ordered.Count; i++)
        {
            if (ordered[i].Stamp is null)
                break;
            removed.Add(ordered[i].Name);
        }

        return removed;
    }

    [GeneratedRegex(@"^chiaki_session_(.*)\.log$")]
    private static partial Regex NameRegex();
}
