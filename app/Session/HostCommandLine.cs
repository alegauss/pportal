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
        new("--ratchet", "", "list the shipped tasks no assertion names, each with its symptom"),
        new("--controllers", "", "print what SDL sees, for a pad that is plugged in now"),
        new("--capture-controller", "", "log presses for twenty seconds; add --analog for the sticks"),
        new("--analog", "", "with --capture-controller: include the axes, which flood the log"),
        new("--capture-mapping", "[path]", "render the mapping screen off-screen to a PNG"),
        new("--map-controller", "", "open the mapping screen against a real pad"),
        new("--dcomp-demo", "", "show what one window composes, which PP163 is answered by looking at"),
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
