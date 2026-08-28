using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP483: the census that falsifies "this port does not edit lib/".
///
/// Two defects in lib/ are recorded rather than repaired, and both cited that sentence as the
/// reason. It is not true, and it was not true when either was written: files under lib/src carry
/// PP markers naming this port's own repairs, from PP68 in bitstream.c through a dozen in ctrl.c.
///
/// The practice that replaced the policy has a shape - patch the C, move the managed model and its
/// assertions in the same commit, and let the drift check hold the pair together - so what the
/// sentence was holding up is a decision and not an impossibility. THIS DOES NOT TAKE THAT
/// DECISION. Repairing either defect still costs something, and PP107's five predicates with
/// PP109's five C assertions exist to pin the present behaviour. Whether they move is the author's
/// call.
///
/// What this does is stop the false reason coming back. §PP107 named the problem itself: prose
/// does not go red. So the census is asserted rather than written down.
/// </summary>
public static partial class LibRepairCensus
{
    /// <summary>The C half, relative to the repository root.</summary>
    public const string SourceRelativePath = @"lib\src";

    /// <summary>The managed half, whose rationale files are what went stale.</summary>
    public const string ManagedRelativePath = "app";

    /// <summary>
    /// This file, excluded from the scan below.
    ///
    /// It has to be: the needles are declared here as literals, so a guard that read its own
    /// declaration would report the claim it exists to forbid. One file, named, rather than a
    /// cleverness that hides what is being matched.
    /// </summary>
    public const string CensusFileName = "LibRepairCensus.cs";

    /// <summary>
    /// The ways a rationale states the premise this census falsifies.
    ///
    /// More than one, because the sentence is a claim and not a spelling - the point is that no
    /// reason in the port rests on lib/ being untouched, however it is phrased.
    /// </summary>
    public static IReadOnlyList<string> FalsePremises { get; } =
    [
        "does not edit lib/",
        "never edits lib/",
        "cannot edit lib/",
        "does not patch lib/",
    ];

    /// <summary>lib/src, or null outside a checkout.</summary>
    public static string? LocateSource() => SanitizerSource.LocateDirectory(SourceRelativePath);

    /// <summary>app/, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativePath);

    /// <summary>The distinct task ids a text names, in the order they first appear.</summary>
    public static IReadOnlyList<string> TaskIdsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ids = new List<string>();
        foreach (Match match in TaskIdRegex().Matches(text))
        {
            if (!ids.Contains(match.Value, StringComparer.Ordinal))
                ids.Add(match.Value);
        }

        return ids;
    }

    /// <summary>
    /// The files under lib/src that carry a marker naming one of this port's tasks.
    ///
    /// Enumerated recursively, because lib/src has a remote/ subtree that a flat glob misses - and
    /// two of the repairs counted here landed in it.
    /// </summary>
    public static IReadOnlyList<string> RepairedFiles()
    {
        if (LocateSource() is not { } root)
            return [];

        var repaired = new List<string>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".c", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            if (TaskIdRegex().IsMatch(File.ReadAllText(path)))
                repaired.Add(Path.GetRelativePath(root, path));
        }

        return repaired;
    }

    /// <summary>Every task id named by a marker anywhere under lib/src.</summary>
    public static IReadOnlyList<string> RepairTaskIds()
    {
        if (LocateSource() is not { } root)
            return [];

        var ids = new List<string>();
        foreach (string relative in RepairedFiles())
        {
            foreach (string id in TaskIdsIn(File.ReadAllText(Path.Combine(root, relative))))
            {
                if (!ids.Contains(id, StringComparer.Ordinal))
                    ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Whether a text states the premise, in any of the ways it has been spelled.</summary>
    public static bool StatesTheFalsePremise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FalsePremises.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The managed files whose prose still rests a reason on lib/ being untouched.
    ///
    /// Empty is the passing answer. Scanned over app/ alone: the rationale files under docs/ quote
    /// the premise in order to say it is false, which is the opposite problem.
    /// </summary>
    public static IReadOnlyList<string> FilesStatingTheFalsePremise()
    {
        if (LocateManaged() is not { } root)
            return [];

        var offenders = new List<string>();
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).Equals(CensusFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            if (StatesTheFalsePremise(File.ReadAllText(path)))
                offenders.Add(Path.GetRelativePath(root, path));
        }

        return offenders;
    }

    [GeneratedRegex(@"\bPP[0-9]+\b")]
    private static partial Regex TaskIdRegex();
}
