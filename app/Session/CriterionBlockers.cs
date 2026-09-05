using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One id a criterion names as something still to happen, and where it was named.</summary>
/// <param name="About">The task whose criterion names it.</param>
/// <param name="Lead">That criterion's lead, so a failure says which sentence.</param>
/// <param name="Named">The id the sentence waits on.</param>
public readonly record struct CriterionBlocker(string About, string Lead, string Named);

/// <summary>
/// PP690: a criterion that says it waits for a task the ledger already holds.
///
/// PP295's fourth criterion said the four files could not leave "until PP28 stops it - and PP28 is
/// what waits on the three criteria above". PP28 had shipped, and what it shipped was three modelled
/// joins; none of them stops session.c calling the stream connection. PP639 had already released the
/// DEP, so the graph was right and only the prose was wrong - and prose is the half no check reads.
///
/// THE SHAPE IS PP584'S, ONE FIELD OVER. That one holds a deletion line's caller claim against a
/// fact about the tree; this holds a criterion's blocker against the ledger. Both exist because a
/// sentence in these files is read by a person deciding what work costs, and a sentence naming a
/// finished task understates it to zero.
///
/// WHAT COUNTS AS WAITING is deliberately narrow: an id in a phrase that says something has to
/// happen first. A criterion may name a shipped task freely for what it DELIVERED - "Met by PP663",
/// "which PP669 mapped" - and most of them do, because that is how this port records what closed a
/// thing. So the check reads the verb around the id and not the id alone, and the phrases it knows
/// are listed rather than guessed at.
/// </summary>
public static class CriterionBlockers
{
    /// <summary>Where the criteria are.</summary>
    public const string RelativePath = @"docs\ROADMAP.md";

    /// <summary>And the ledger they are checked against.</summary>
    public const string LedgerRelativePath = @"docs\CHANGELOG.md";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// The phrases that make an id a BLOCKER rather than a citation.
    ///
    /// Each one says the named task has still to do something. A criterion citing what a task
    /// already delivered uses none of them, which is what keeps this check off the fourteen
    /// criteria that name a shipped id perfectly correctly.
    /// </summary>
    public static IReadOnlyList<string> WaitingPhrases { get; } =
    [
        "until",
        "waits on",
        "waits for",
        "cannot land until",
        "blocked by",
        "once",
    ];

    /// <summary>Every `## Done when — PPn` heading's id, with the criteria under it.</summary>
    public static IReadOnlyList<CriterionBlocker> BlockersIn(string roadmap, IReadOnlySet<string> shipped)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(shipped);

        var found = new List<CriterionBlocker>();
        string? about = null;
        string? lead = null;
        var body = new System.Text.StringBuilder();

        void Close()
        {
            if (about is null || lead is null)
                return;

            foreach (string named in WaitedOnIn(body.ToString()))
            {
                if (shipped.Contains(named))
                    found.Add(new CriterionBlocker(about, lead, named));
            }

            lead = null;
            body.Clear();
        }

        foreach (string raw in roadmap.ReplaceLineEndings("\n").Split('\n'))
        {
            Match heading = Regex.Match(raw, @"^##\s+Done when\s+[—-]\s+(PP[0-9]+)\s*$");
            if (heading.Success)
            {
                Close();
                about = heading.Groups[1].Value;
                continue;
            }

            if (raw.StartsWith("##", StringComparison.Ordinal))
            {
                Close();
                about = null;
                continue;
            }

            if (about is null)
                continue;

            Match bullet = Regex.Match(raw, @"^-\s+\*\*(.+?)\*\*\s*(.*)$");
            if (bullet.Success)
            {
                Close();
                lead = bullet.Groups[1].Value;
                body.Append(bullet.Groups[2].Value);
                continue;
            }

            if (lead is not null)
                body.Append(' ').Append(raw.Trim());
        }

        Close();
        return found;
    }

    /// <summary>
    /// PP728: one criterion's prose, joined back into a line, or null where it is not there.
    ///
    /// Here rather than in a second reader, for CFunction's reason: a parser that looks general
    /// gets reused and one named after a subsystem gets copied, and the copy is the version that
    /// drifts. This class already walks the Done-when headings and their bullets.
    /// </summary>
    /// <param name="about">The task the criterion belongs to, e.g. PP295.</param>
    /// <param name="lead">Its lead, which is how a criterion is addressed.</param>
    public static string? TextOf(string roadmap, string about, string lead)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentException.ThrowIfNullOrEmpty(about);
        ArgumentException.ThrowIfNullOrEmpty(lead);

        string? heading = null;
        string? found = null;
        var body = new System.Text.StringBuilder();

        foreach (string raw in roadmap.ReplaceLineEndings("\n").Split('\n'))
        {
            Match section = Regex.Match(raw, @"^##\s+Done when\s+[—-]\s+(PP[0-9]+)\s*$");
            if (section.Success)
            {
                if (found is not null)
                    return body.ToString();

                heading = section.Groups[1].Value;
                continue;
            }

            if (raw.StartsWith("##", StringComparison.Ordinal))
            {
                if (found is not null)
                    return body.ToString();

                heading = null;
                continue;
            }

            if (heading is null)
                continue;

            Match bullet = Regex.Match(raw, @"^-\s+\*\*(.+?)\*\*\s*(.*)$");
            if (bullet.Success)
            {
                if (found is not null)
                    return body.ToString();

                if (heading == about && bullet.Groups[1].Value == lead)
                {
                    found = lead;
                    body.Append(bullet.Groups[2].Value);
                }

                continue;
            }

            if (found is not null)
                body.Append(' ').Append(raw.Trim());
        }

        return found is null ? null : body.ToString();
    }

    /// <summary>
    /// The ids one criterion's prose says it is waiting for.
    ///
    /// A phrase and then an id, within the same sentence: the phrase has to reach the id rather than
    /// merely share a paragraph with it, because "Met by PP663 ... until the criterion above" would
    /// otherwise read as waiting on PP663.
    /// </summary>
    public static IReadOnlyList<string> WaitedOnIn(string criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        var found = new List<string>();

        foreach (string sentence in criterion.Split(['.', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string phrase in WaitingPhrases)
            {
                int at = sentence.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    continue;

                foreach (Match id in Regex.Matches(sentence[at..], @"\bPP[0-9]+\b"))
                {
                    if (!found.Contains(id.Value))
                        found.Add(id.Value);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Every id the ledger records as WHOLLY shipped.
    ///
    /// PP666: the id has to be followed by the closing bold, and that is the whole of what took a
    /// second reading. `ship --part` writes its entry as `**PP295 (the run's ordering)**` and leaves
    /// the line open, so a pattern stopping at the digits counts a line that is still being worked
    /// as finished - which made an open line's criterion read as waiting on something shipped and
    /// turned this check red about a sentence that was right.
    ///
    /// Found by PP666's driver rather than by this check's own tests, which is the point that line
    /// makes: a reader written from the same idea as the thing it reads inherits its blind spot.
    /// </summary>
    public static IReadOnlySet<string> ShippedIn(string ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return new HashSet<string>(
            Regex.Matches(ledger, @"^- ✅ \*\*(PP[0-9]+)\*\*", RegexOptions.Multiline)
                .Select(one => one.Groups[1].Value),
            StringComparer.Ordinal);
    }
}
