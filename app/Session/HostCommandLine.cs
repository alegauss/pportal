using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One flag the host answers, as it is written and as it is described.</summary>
/// <param name="Name">The flag itself, including its dashes.</param>
/// <param name="Argument">What follows it, or the empty string where nothing does.</param>
/// <param name="Summary">One line: what running it does, not how.</param>
public readonly record struct HostFlag(string Name, string Argument, string Summary);

/// <summary>
/// PP306: the flags the host answers, in one place, held against the ones it actually matches.
///
/// Seven of them arrived one at a time - a selftest, a controller dump, a compositor demo, two
/// captures, a mapping screen and a recount - and each was documented in the summary of the method
/// that runs it, which is the right place for how it works and no place at all for finding out it
/// exists. The cost is not knowing a flag; it is not knowing which one answers the question in
/// front of you. --recount answers what test.cmd answers, before the work rather than after it,
/// and a person who does not know it is there pays PP304's tax anyway.
///
/// AN UNRECOGNISED FLAG USED TO OPEN THE WINDOW
/// --------------------------------------------
/// OnStartup matched each flag in turn and fell through to StartupUri, so --self-test or --recounts
/// launched the application - and on a machine with no console that was the whole of the feedback.
/// A flag that starts with two dashes and is not one of these is now a refusal with the list and a
/// non-zero exit, which is the same answer every other command line gives.
///
/// The list is held against the source that dispatches on it, by
/// <see cref="FlagsMatchedIn"/>. A written-once list that nothing compares is the drift this port
/// files tasks about, and it would go stale on the first flag added in a hurry.
/// </summary>
public static partial class HostCommandLine
{
    /// <summary>Where the dispatch is, relative to the repository root.</summary>
    public const string SourceRelativePath = @"app\App.xaml.cs";

    /// <summary>The source, or null outside a checkout.</summary>
    public static string? LocateSource() => SanitizerSource.LocateRelative(SourceRelativePath);

    /// <summary>
    /// Every flag the host answers, in the order a reader wants them: the gates first, then the
    /// things you run because hardware is plugged in, then the two that draw.
    /// </summary>
    public static IReadOnlyList<HostFlag> Flags { get; } =
    [
        new("--selftest", "", "run the host's own assertions and exit with the verdict"),
        new("--recount", "", "check the sizes the backlog states, and print what corrects each"),
        new("--apply", "", "with --recount: run those corrections through roadkeep, stopping on a refusal"),
        new("--select-corpus", "<in> <out>", "keep the entries a replay can expect, and report what was dropped"),
        new("--ratchet", "[id]", "list the shipped tasks no assertion names; with an id, where it is named"),
        new("--backlog", "", "split the open lines into what can be started and what waits on something absent"),
        new("--controllers", "", "print what SDL sees, for a pad that is plugged in now"),
        new("--capture-controller", "", "log presses for twenty seconds; add --analog for the sticks"),
        new("--analog", "", "with --capture-controller: include the axes, which flood the log"),
        new("--capture-mapping", "[path]", "render the mapping screen off-screen to a PNG"),
        new("--record", "[path]", "record this session's exchange to a file, for PP297's replay"),
        new("--capture-exchange", "[path]", "wake a registered console, run one session and record the exchange"),
        new("--capture-datagrams", "[path]", "the same session, recording takion's arrivals and their times"),
        new("--console", "[name]", "with either capture: which registered console, by nickname"),
        new("--capture-seconds", "[n]", "with either capture: how long a sample, which sets the hold too"),
        new("--via", "[address|relay]", "with --capture-datagrams: go through an address, or 'relay' to run one here and keep whole datagrams"),
        new("--replay-datagrams", "<path>", "replay a datagram capture through the managed receive path"),
        new("--timed", "", "with --replay-datagrams: time the MAC gate against the C over the capture"),
        new("--map-controller", "", "open the mapping screen against a real pad"),
        new("--consoles", "", "open the console list, and connect to a registered console from it"),
        new("--dcomp-demo", "", "show what one window composes, which PP163 is answered by looking at"),
        new("--topmost", "", "with --dcomp-demo: the control, asking the visual to cover WPF instead"),
        new("--layers", "", "with --dcomp-demo: the overlay PP319 chose, over the video plane"),
    ];

    /// <summary>
    /// What asks for the list. Four spellings, because a person who does not know the flags does
    /// not know which of these this program wanted either.
    /// </summary>
    public static IReadOnlySet<string> HelpFlags { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--help", "-h", "-?", "/?" };

    /// <summary>Whether the arguments ask for the list.</summary>
    public static bool IsHelp(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Any(HelpFlags.Contains);
    }

    /// <summary>
    /// Arguments that look like a flag and are not one.
    ///
    /// Two dashes and nothing else, deliberately. A bare word is the argument --capture-mapping
    /// takes, and a single dash is not a shape this host has ever used - refusing either would
    /// turn a path into an error.
    /// </summary>
    public static IReadOnlyList<string> Unrecognised(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var known = new HashSet<string>(Flags.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        known.UnionWith(HelpFlags);

        return [.. args.Where(a => a.StartsWith("--", StringComparison.Ordinal) && !known.Contains(a))];
    }

    /// <summary>Whether the arguments carry this flag, spelled either way.</summary>
    public static bool Has(IReadOnlyList<string> args, string flag)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(flag);

        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// PP329: the value a flag takes, or null where it was omitted - INCLUDING where what follows
    /// is another flag.
    ///
    /// Three flags here take an optional argument and two of them used to read it as "whatever is
    /// next, if there is anything at all". Nothing asked whether that was a flag, so
    /// `--capture-mapping --analog` wrote a PNG called "--analog" and ran no capture, and
    /// `--ratchet --selftest` looked up a task whose id was "--selftest" and exited without
    /// selftesting. Both silently: the argument was accepted, so nothing was refused, and PP306's
    /// unknown-flag check cannot help because both spellings ARE known flags.
    ///
    /// TWO DASHES AND NOT ONE, for the reason PP306 gives about <see cref="Unrecognised"/>: a bare
    /// word is what these flags legitimately take, and this port has never spelled anything with a
    /// single dash - so refusing one would turn a relative path into an error for no case that
    /// exists. A value that must start with a dash is still reachable, by putting it after a flag
    /// that takes none.
    ///
    /// PP327 made this decision locally for --record when it was the only caller. It is three now,
    /// which is when it stops being a local rule.
    /// </summary>
    public static string? ValueAfter(IReadOnlyList<string> args, string flag)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(flag);

        for (int i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                continue;

            string? next = i + 1 < args.Count ? args[i + 1] : null;
            return next is not null && !next.StartsWith("--", StringComparison.Ordinal) ? next : null;
        }

        return null;
    }

    /// <summary>
    /// PP297: where <c>--record</c> writes, or null where it was not asked for.
    ///
    /// The flag is the last thing that task was reduced to - "a flag rather than a project" - and
    /// this is the half of it that can be checked without a console in the room.
    ///
    /// DEFAULTED WITH A TIMESTAMP, because the alternative is one name reused. A recording is made
    /// to be compared with another one, and a run that silently replaced the file it is about to be
    /// diffed against would be the worst possible failure of this feature.
    /// </summary>
    /// <param name="defaultDirectory">Where an omitted path lands - the session log directory.</param>
    /// <param name="now">Stamped into the default name. Passed in so this can be asserted about.</param>
    public static string? RecordingPath(
        IReadOnlyList<string> args, string defaultDirectory, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(defaultDirectory);

        if (!Has(args, "--record"))
            return null;

        if (ValueAfter(args, "--record") is string path)
            return path;

        return Path.Combine(
            defaultDirectory,
            $"exchange-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
    }

    /// <summary>The list, as it is printed.</summary>
    public static string Usage()
    {
        var text = new StringBuilder();
        text.AppendLine("ChiakiNg.exe - PlayStation remote play, Windows only.");
        text.AppendLine();
        text.AppendLine("With no flag it opens the application. Otherwise:");
        text.AppendLine();

        int width = Flags.Max(f => (f.Name + " " + f.Argument).TrimEnd().Length);

        foreach (HostFlag flag in Flags)
        {
            string spelled = (flag.Name + " " + flag.Argument).TrimEnd();
            text.Append("  ").Append(spelled.PadRight(width)).Append("  ").AppendLine(flag.Summary);
        }

        return text.ToString();
    }

    /// <summary>
    /// Every flag the dispatch actually matches, read out of it.
    ///
    /// The join is the literal: OnStartup compares each argument against a quoted "--name", so what
    /// this finds is what the program answers rather than what a list says it answers. A flag added
    /// with no line beside it is then a red assertion in the commit that adds it, which is the only
    /// moment it is cheap to write one.
    /// </summary>
    public static IReadOnlySet<string> FlagsMatchedIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match literal in FlagLiteralRegex().Matches(source))
            found.Add(literal.Groups["flag"].Value);

        return found;
    }

    // "--selftest" and the rest, as they are written in the comparisons.
    [GeneratedRegex(@"""(?<flag>--[a-z][a-z0-9-]*)""")]
    private static partial Regex FlagLiteralRegex();
}
