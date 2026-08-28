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

    [GeneratedRegex(@"\(deps:\s*(?<ids>[^)]*)\)")]
    private static partial Regex DepsGroupRegex();

    [GeneratedRegex(@"\b[A-Z]{2}\d+\b")]
    private static partial Regex IdRegex();
}
