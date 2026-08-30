using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP582: every partially-shipped line has a definition of done.
///
/// A line marked partial means some of it shipped and some has not, and that is precisely the state
/// where "how much is left" is a question somebody asks. Without criteria there is no answer: the
/// only measure left is a count that reads its full value until the end, which is the failure
/// roadkeep.toml declares [criteria] to fix - "a number that only leaves zero at the finish cannot
/// tell half done from not started".
///
/// IT WAS DECLARED AND USED ONCE. PP33 had five criteria and the other five partial lines had none,
/// so the table fixed the problem for one line in six. This holds the rest: PP11, PP27, PP322, PP46
/// and PP76 now carry theirs, derived from what their own sections already commit to rather than
/// invented here.
///
/// A HARD ASSERTION, NOT A CEILING. The ratchet counts a debt that may fall and may not rise
/// because it inherited ninety-odd; this inherited five and they are paid, so there is nothing to
/// ratchet down from. A partial line arriving without criteria is a regression, not a backlog.
/// </summary>
public static partial class PartialCriteria
{
    /// <summary>Where both the markers and the lists live.</summary>
    public const string RelativePath = @"docs\ROADMAP.md";

    /// <summary>The roadmap, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The marker roadkeep.toml gives a partially-shipped line.</summary>
    public const string PartialMarker = "⏳";

    /// <summary>The ids of every line marked partial.</summary>
    public static IReadOnlyList<string> PartialIds(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        return [.. Partial().Matches(roadmap).Select(one => one.Groups["id"].Value)];
    }

    /// <summary>The ids that have a criteria list of their own.</summary>
    public static IReadOnlySet<string> IdsWithCriteria(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        return Heading().Matches(roadmap)
            .Select(one => one.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Partial lines carrying no definition of done.</summary>
    public static IReadOnlyList<string> WithoutCriteria(string roadmap)
    {
        ArgumentNullException.ThrowIfNull(roadmap);

        IReadOnlySet<string> have = IdsWithCriteria(roadmap);
        return [.. PartialIds(roadmap).Where(id => !have.Contains(id))];
    }

    /// <summary>A partial task line, by its marker and id.</summary>
    [GeneratedRegex(@"^\s*-\s*⏳\s*\*\*(?<id>PP\d+)\*\*", RegexOptions.Multiline)]
    private static partial Regex Partial();

    /// <summary>A per-task criteria heading, as roadkeep writes it.</summary>
    [GeneratedRegex(@"^##\s*Done when\s*[—-]\s*(?<id>PP\d+)", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
