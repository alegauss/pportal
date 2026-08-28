using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP312: what a line waits on that is not another line, held against what the project declared.
///
/// Deps could not express it and were right not to. PP23's deps are satisfied and PP297's are
/// empty, so both are ready in the only sense a dep graph means it - and neither can be started,
/// because starting them needs a console reaching the stream. `brief` answered PP23 on four
/// consecutive sessions for that reason.
///
/// roadkeep's answer is `[requirements]`, and it is two files that have to agree with no reader in
/// common: roadkeep.toml declares the names, docs/ROADMAP.md spells them on the lines that wait.
/// A line requiring something undeclared is a typo that reads as a real blocker; a declared
/// requirement nothing uses is a blocker that was lifted and never removed, which is worse - it
/// says the project is still waiting for a thing it has.
/// </summary>
public static partial class BacklogRequirements
{
    /// <summary>Where the requirement names are declared.</summary>
    public const string ConfigRelativePath = "roadkeep.toml";

    /// <summary>And where lines say they wait on one.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The config, or null outside a checkout.</summary>
    public static string? LocateConfig() => SanitizerSource.LocateRelative(ConfigRelativePath);

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? LocateRoadmap() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>
    /// The names <c>[requirements] declared</c> holds.
    ///
    /// Read from the array rather than from the whole file, because every one of them is also
    /// written in the prose above it explaining why it is there - and a comment is not a
    /// declaration.
    /// </summary>
    public static IReadOnlySet<string> Declared(string config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var names = new SortedSet<string>(StringComparer.Ordinal);

        Match array = DeclaredArrayRegex().Match(config);
        if (!array.Success)
            return names;

        foreach (Match name in QuotedRegex().Matches(WithoutComments(array.Groups["items"].Value)))
            names.Add(name.Groups["name"].Value);

        return names;
    }

    /// <summary>
    /// The array's text with its TOML comments removed, which the quotes have to be counted after.
    ///
    /// Each entry in this project's table carries a paragraph above it saying why the thing is
    /// absent, and one of those paragraphs quotes a sentence. An unpaired quote inside a comment
    /// desynchronises every pair after it, so the first read of the real file found one name that
    /// was a fragment of a comment and none of the four that were declared - which is this reader's
    /// own subject, arriving from the direction its summary had just ruled out.
    ///
    /// A `#` inside a requirement NAME would be eaten with the comment. Requirement names are
    /// identifiers and carry none, and a name that did would be unspellable in a task line's
    /// `(requires: …)` annotation anyway.
    /// </summary>
    public static string WithoutComments(string toml)
    {
        ArgumentNullException.ThrowIfNull(toml);

        return string.Join('\n', toml
            .Split('\n')
            .Select(line => line.IndexOf('#', StringComparison.Ordinal) is int at && at >= 0
                ? line[..at]
                : line));
    }

    /// <summary>Every requirement a roadmap line says it waits on.</summary>
    public static IReadOnlySet<string> Used(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match line in RequiresRegex().Matches(roadmap))
        {
            foreach (string name in line.Groups["names"].Value.Split(','))
            {
                string trimmed = name.Trim();
                if (trimmed.Length > 0)
                    names.Add(trimmed);
            }
        }

        return names;
    }

    /// <summary>
    /// A phrase a line's own prose uses, and the requirement it means.
    ///
    /// Deliberately narrow, and shaped like necessity rather than like mention. This project's
    /// backlog says "console" constantly - the whole port is about one - so a phrase that matched
    /// that would flag every line and be turned off within a week. "a live console" is a line saying
    /// it cannot be finished without hardware, which is a different sentence.
    /// </summary>
    public static IReadOnlyList<(string Phrase, string Requirement)> ProseNames { get; } =
    [
        ("live console", "console"),
        ("a person looking", "a-person-looking"),
    ];

    /// <summary>
    /// An open line whose prose names something it does not declare it waits on.
    /// </summary>
    /// <param name="Id">The line.</param>
    /// <param name="Requirement">What it needs and did not say.</param>
    /// <param name="Phrase">The words in its own text that say so.</param>
    public readonly record struct RequirementGap(string Id, string Requirement, string Phrase);

    /// <summary>
    /// Every open line whose prose names a resource its <c>(requires: …)</c> group leaves out.
    ///
    /// PP486: the third check, and the direction the other two do not cover. Declared-against-used
    /// and used-against-declared both hold while a line says in words that it needs hardware and
    /// declares nothing - and `pick`, which reads the group and not the sentence, offers it as the
    /// next ready thing to do. PP481 was offered that way from the moment it was filed.
    ///
    /// Only ROADMAP.md is read, so only open lines are: a delivered line's requirement has been met
    /// by definition, and the ledger keeps the sentence for the record rather than as a claim.
    /// </summary>
    public static IReadOnlyList<RequirementGap> Gaps(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        var gaps = new List<RequirementGap>();

        foreach (string line in roadmap.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            Match id = LineIdRegex().Match(line);
            if (!id.Success)
                continue;

            // Reusing the same reader the used-set is built from, so the two cannot disagree about
            // what a line declares.
            IReadOnlySet<string> declares = Used(line);

            foreach ((string phrase, string requirement) in ProseNames)
            {
                int at = line.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                if (at < 0 || declares.Contains(requirement))
                    continue;

                if (IsReportedSpeech(line, id.Groups["id"].Value, at))
                    continue;

                gaps.Add(new RequirementGap(id.Groups["id"].Value, requirement, phrase));
            }
        }

        return gaps;
    }

    /// <summary>
    /// Whether the phrase sits in a clause about ANOTHER task rather than about this one.
    ///
    /// A line filed to fix this very gap has to quote the words to say what is wrong - the line that
    /// added this check does exactly that, and flagged itself on the first run. A guard that read a
    /// report as a need would flag every line ever written about requirements, and a guard that
    /// flags the honest lines is one somebody turns off.
    ///
    /// So an id other than the line's own, appearing BEFORE the phrase, makes it reported speech.
    /// Before, not anywhere: a line that needs a console and cites a neighbour afterwards is still
    /// making its own claim, and is still caught. The id reader is
    /// <see cref="LibRepairCensus.TaskIdsIn"/> rather than a second one of this class's own.
    /// </summary>
    private static bool IsReportedSpeech(string line, string ownId, int phraseAt)
        => LibRepairCensus.TaskIdsIn(line[..phraseAt])
            .Any(id => !id.Equals(ownId, StringComparison.Ordinal));

    // - 📋 **PP481** (deps: —) **symptom** — why. → §PP481
    [GeneratedRegex(@"^-\s.*?\*\*(?<id>PP[0-9]+)\*\*")]
    private static partial Regex LineIdRegex();

    // declared = [ "console", ... ]  - up to the closing bracket, comments and all.
    [GeneratedRegex(@"declared\s*=\s*\[(?<items>[^\]]*)\]", RegexOptions.Singleline)]
    private static partial Regex DeclaredArrayRegex();

    [GeneratedRegex("\"(?<name>[^\"]+)\"")]
    private static partial Regex QuotedRegex();

    // (requires: console) and (requires: a, b)
    [GeneratedRegex(@"\(requires:\s*(?<names>[^)]+)\)")]
    private static partial Regex RequiresRegex();
}
