using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One `roadkeep-remaining` query a section declares.</summary>
/// <param name="Glob">The paths it counts over, as the fence spells them.</param>
/// <param name="Pattern">The regex a matching line has to contain.</param>
public readonly record struct RemainingQuery(string Glob, string Pattern);

/// <summary>A count the backlog states for one of those queries.</summary>
/// <param name="Task">The task id, e.g. PP30.</param>
/// <param name="Stated">What the prose says the query reads.</param>
/// <param name="Line">Where it says it, for the message.</param>
public readonly record struct StatedCount(string Task, int Stated, int Line);

/// <summary>
/// PP447: the counts a `roadkeep-remaining` query already answers, held against the prose.
///
/// Two sections declare a query. §PP33's read 420 and the prose said 420. §PP30's reads 13 and the
/// prose said "14 on 2026-08-16" - one site removed since, which is progress nobody recorded and a
/// number nobody rechecked.
///
/// PP443's GUARD CANNOT SEE THIS. It reads the phrase "N lines"; this is "N sites". And
/// <see cref="CountedClaims"/> needs a filename beside the number, which a query count has none of.
/// Adjacent class, not the same one.
///
/// THE QUERY IS THE ORACLE AND IT IS IN THE FILE, so there is no table here and no arithmetic. The
/// fence carries `glob :: regex` and this runs it. Verified against roadkeep before being written:
/// 46 files and 13 matching lines for PP30's, which is what `roadkeep remaining PP30` answers - so a
/// "site" is a matching LINE and not a file or a call.
///
/// THE COUNT NEED NOT BE IN THE DECLARING SECTION. PP33's query is declared in §PP33 and its count is
/// stated in §PP340, which is why this scans the whole document for `remaining PPn reads N` rather
/// than looking inside the section that carries the fence. Found by trying the other way and having
/// roadkeep refuse the amend.
/// </summary>
public static partial class RemainingQueries
{
    /// <summary>Where the queries and the counts both live.</summary>
    public const string ImprovementsRelativePath = @"docs\IMPROVEMENTS.md";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(ImprovementsRelativePath);

    /// <summary>
    /// Every query the document declares, by the task whose section carries the fence.
    ///
    /// The fence's own shape: a ```roadkeep-remaining block whose one content line is
    /// `glob :: pattern`. The task is the nearest section anchor above it.
    /// </summary>
    public static IReadOnlyDictionary<string, RemainingQuery> Declared(string improvements)
    {
        ArgumentNullException.ThrowIfNull(improvements);

        var found = new Dictionary<string, RemainingQuery>(StringComparer.Ordinal);
        string[] lines = improvements.Split('\n');
        string? anchor = null;
        bool inFence = false;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');

            Match section = AnchorRegex().Match(line);
            if (section.Success)
            {
                anchor = section.Groups["id"].Value;
                continue;
            }

            if (line.StartsWith("```roadkeep-remaining", StringComparison.Ordinal))
            {
                inFence = true;
                continue;
            }

            if (inFence)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = false;
                    continue;
                }

                Match query = QueryRegex().Match(line);
                if (query.Success && anchor is not null)
                {
                    found[anchor] = new RemainingQuery(
                        query.Groups["glob"].Value.Trim(), query.Groups["pattern"].Value.Trim());
                }
            }
        }

        return found;
    }

    /// <summary>Every count the document states, wherever it states it.</summary>
    public static IReadOnlyList<StatedCount> Stated(string improvements)
    {
        ArgumentNullException.ThrowIfNull(improvements);

        var stated = new List<StatedCount>();
        string[] lines = improvements.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match said in StatedRegex().Matches(lines[i]))
            {
                stated.Add(new StatedCount(
                    said.Groups["task"].Value,
                    int.Parse(said.Groups["count"].Value, CultureInfo.InvariantCulture),
                    i + 1));
            }
        }

        return stated;
    }

    /// <summary>
    /// What a query answers over the tree: matching LINES, which is roadkeep's own unit.
    ///
    /// Null where the glob names no directory this checkout has - absent is not zero, and a check
    /// that read zero from an absent tree would agree with any prose at all.
    /// </summary>
    public static int? Run(string root, RemainingQuery query)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        (string directory, string extension) = Split(query.Glob);
        string start = Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(start))
            return null;

        var pattern = new Regex(query.Pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
        int sites = 0;

        foreach (string file in Directory.EnumerateFiles(start, "*" + extension, SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(file))
            {
                if (pattern.IsMatch(line))
                    sites++;
            }
        }

        return sites;
    }

    /// <summary>
    /// Where a stated count and its query disagree, as sentences.
    ///
    /// A stated count for a task that declares no query is reported too: it is a number about a
    /// question nobody wrote down.
    /// </summary>
    public static IReadOnlyList<string> Disagreements(string root, string improvements)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(improvements);

        IReadOnlyDictionary<string, RemainingQuery> queries = Declared(improvements);
        var apart = new List<string>();

        foreach (StatedCount said in Stated(improvements))
        {
            if (!queries.TryGetValue(said.Task, out RemainingQuery query))
            {
                apart.Add($"line {said.Line} says {said.Task} reads {said.Stated} and no section "
                    + "declares a query for it");
                continue;
            }

            if (Run(root, query) is not { } actual)
                continue;

            if (actual != said.Stated)
            {
                apart.Add($"line {said.Line} says {said.Task} reads {said.Stated} and it reads "
                    + $"{actual}");
            }
        }

        return apart;
    }

    // "lib/src/**/*.c" -> ("lib/src", ".c"). The recursive marker is what SearchOption already does,
    // so it is stripped rather than interpreted.
    private static (string Directory, string Extension) Split(string glob)
    {
        int star = glob.IndexOf('*', StringComparison.Ordinal);
        string head = star < 0 ? glob : glob[..star];
        string extension = Path.GetExtension(glob);

        return (head.TrimEnd('/', '\\'), extension.Length == 0 ? ".c" : extension);
    }

    // ### §PP30 Reed-Solomon, by hand
    [GeneratedRegex(@"^#{2,4}\s+§(?<id>PP\d+)\b")]
    private static partial Regex AnchorRegex();

    // lib/src/**/*.c :: jerasure|galois_
    [GeneratedRegex(@"^\s*(?<glob>[^\s:]+)\s*::\s*(?<pattern>.+?)\s*$")]
    private static partial Regex QueryRegex();

    // `remaining PP30` reads 13
    [GeneratedRegex(@"`remaining (?<task>PP\d+)`\s+reads\s+(?<count>\d+)")]
    private static partial Regex StatedRegex();
}
