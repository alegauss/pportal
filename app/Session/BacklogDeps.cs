using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP506: what an open line says it waits on, read from its own deps group.
///
/// <see cref="BacklogRequirements"/> reads the OTHER group - what a line waits on that is not
/// another line. This is the ordinary one, and nothing managed had a reader for it: the two counted
/// claims and the requirements checks all skip past `(deps: …)` on their way to the symptom.
///
/// It exists because a dep that is missing is invisible in exactly the way a requirement that is
/// missing was. Both hold a line back; neither is written anywhere a check reads; and the symptom
/// is the same one PP312 and PP486 describe - a queue offering something as ready that is not.
/// </summary>
public static partial class BacklogDeps
{
    /// <summary>Where the lines are.</summary>
    public const string RoadmapRelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? LocateRoadmap() => SanitizerSource.LocateRelative(RoadmapRelativePath);

    /// <summary>
    /// The ids one line lists as deps, with their markers stripped.
    ///
    /// An em dash for "no deps" yields an empty set rather than a name, which is how roadkeep
    /// spells an empty group.
    /// </summary>
    public static IReadOnlySet<string> Of(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var ids = new SortedSet<string>(StringComparer.Ordinal);

        Match group = DepsGroupRegex().Match(line);
        if (!group.Success)
            return ids;

        foreach (Match id in IdRegex().Matches(group.Groups["ids"].Value))
            ids.Add(id.Value);

        return ids;
    }

    /// <summary>
    /// The open line carrying <paramref name="id"/>, or null where the roadmap has none.
    ///
    /// Null is the shipped case and callers treat it as such: a delivered line's deps have been
    /// met by definition, and the ledger keeps its sentence for the record rather than as a claim.
    /// </summary>
    public static string? LineFor(string roadmap, string id)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentException.ThrowIfNullOrEmpty(id);

        return roadmap
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .FirstOrDefault(line => line.Contains($"**{id}**", StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="id"/> is still an open line.</summary>
    public static bool IsOpen(string roadmap, string id) => LineFor(roadmap, id) is not null;

    /// <summary>
    /// PP28: an open line whose own sentence names an open id its deps group does not.
    ///
    /// The general form of the two hand-written checks above, and PP28 is why it exists. Its why
    /// read "the three together, once PP293, PP294 and PP295 have each landed" while its deps named
    /// three ids, none of them PP295 - so `pick` offered it as the next ready thing, and starting it
    /// meant porting a file another line owns. That is PP312's symptom exactly, arriving through the
    /// group PP486 was not about.
    ///
    /// Prose only, so the answer is about what the AUTHOR wrote. Everything up to the first group is
    /// the marker and the id, the groups themselves are the machine-readable claim this is checking
    /// against, and the pointer after the arrow is derived from the id. What is left is the symptom
    /// and the why, which is where a person says what a line is waiting for.
    ///
    /// A SHIPPED id is not a finding. Most prose names one - "PP322 read that", "PP648 measured
    /// that" - and that is history rather than a wait, so only ids that are still open lines count.
    ///
    /// NEITHER IS A REVERSE EDGE, which is the case this found on its first run. PP63's symptom
    /// names PP46 - "PP46's before cannot be produced at all" - and PP46 already deps on PP63. That
    /// is a line saying what it UNBLOCKS, and the dep this check would ask for is the one that makes
    /// a cycle. So a mention is skipped where the named line already waits on this one.
    ///
    /// AND NEITHER IS AN END STATE, which is the case this check got WRONG before it was written.
    /// PP28's why named PP295 and its deps did not, which reads exactly like the defect above - and
    /// adding the dep turned <see cref="DeletionEndState"/> red, because PP639 had already settled
    /// that PP295's deletion waits on PP28 and the dep is the edge that rule forbids. A line can
    /// name another because its PORT comes first and its LINE closes later, and the two rules have
    /// to compose or this one walks somebody into the edit the other refuses.
    /// </summary>
    /// <returns>Each open id, with the line that names it, keyed by the line's own id.</returns>
    public static IReadOnlyList<(string Id, string Names)> MentionedButNotDepended(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        var found = new List<(string, string)>();

        foreach (string line in Lines(roadmap))
        {
            Match head = LineIdRegex().Match(line);
            if (!head.Success)
                continue;

            string own = head.Groups["id"].Value;
            IReadOnlySet<string> declared = Of(line);

            foreach (Match named in IdRegex().Matches(Prose(line)))
            {
                if (named.Value == own || declared.Contains(named.Value))
                    continue;
                if (LineFor(roadmap, named.Value) is not { } other)
                    continue;

                // The reverse edge: the named line already waits on this one, so the mention is
                // what this line unblocks and the dep it would ask for is a cycle.
                if (Of(other).Contains(own))
                    continue;

                // PP639's edge: the named line's END STATE waits on this one. Its port can still
                // come first, which is what the prose is saying, and the dep would be the one that
                // rule exists to refuse.
                if (DeletionEndState.WaitsOn.TryGetValue(named.Value, out IReadOnlyList<string>? after)
                    && after.Contains(own, StringComparer.Ordinal))
                {
                    continue;
                }

                found.Add((own, named.Value));
            }
        }

        return found;
    }

    /// <summary>
    /// The author's half of a line: after the last parenthesised group, before the pointer.
    ///
    /// Cutting at the arrow is what keeps a line's own derived pointer out of the answer, and
    /// cutting after the groups is what keeps the deps from being read as prose that mentions them.
    /// </summary>
    public static string Prose(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        int start = 0;
        foreach (Match group in GroupRegex().Matches(line))
            start = Math.Max(start, group.Index + group.Length);

        int end = line.IndexOf('→', StringComparison.Ordinal);
        if (end < 0)
            end = line.Length;

        return end > start ? line[start..end] : "";
    }

    /// <summary>The roadmap's lines, however the checkout spells its endings.</summary>
    private static IEnumerable<string> Lines(string roadmap)
        => roadmap.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    [GeneratedRegex(@"\(deps:\s*(?<ids>[^)]*)\)")]
    private static partial Regex DepsGroupRegex();

    // Both parenthesised groups a line can carry, so the prose starts after whichever came last.
    [GeneratedRegex(@"\((?:deps|requires):[^)]*\)")]
    private static partial Regex GroupRegex();

    // A task line and nothing else: the criteria under "Done when" are prose about a line rather
    // than lines, and reading them here would report a criterion's own reference as a missing dep.
    [GeneratedRegex(@"^- [^ ]+ \*\*(?<id>[A-Z]{2}\d+)\*\*")]
    private static partial Regex LineIdRegex();

    [GeneratedRegex(@"\b[A-Z]{2}\d+\b")]
    private static partial Regex IdRegex();
}
