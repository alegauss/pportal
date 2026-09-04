namespace ChiakiNg.Session;

/// <summary>A member whose leading docstring carries more than one summary.</summary>
/// <param name="Where">The file, repository-relative.</param>
/// <param name="Line">Where the run of `///` lines starts, one-based.</param>
/// <param name="Declares">The line the run sits on, trimmed, so a failure names the member.</param>
/// <param name="Summaries">How many it carries.</param>
public readonly record struct StackedSummary(string Where, int Line, string Declares, int Summaries);

/// <summary>
/// PP643: two `&lt;summary&gt;` elements on one member compile, and the wrong one wins silently.
///
/// The documentation generator takes one and drops the other, and a reader of the source sees both.
/// So a docstring can describe a member two declarations further down while sitting on one it says
/// nothing true about - and the member it belongs to is left with none at all.
///
/// IT IS ALWAYS THE SAME ACCIDENT: somebody inserts a new member directly beneath an existing
/// docstring, and the old one is left stranded at the top of the run. Eleven of the twelve this
/// found were exactly that, and in every one the FIRST block was the orphan. The twelfth was two
/// paragraphs about the same member that should have been one summary with a break in it.
///
/// THE REASON TO CHECK IT HERE rather than treat it as style: the assertion ratchet joins tasks to
/// tests by the id in a test's summary, so a summary attached to the wrong member is a coverage
/// claim made about the wrong thing. Two of the twelve were in test files, and one of those was the
/// docstring naming PP569 sitting over the table test rather than the tool test.
///
/// A SCAN AND NOT A PARSE. Within a member's leading run of `///` lines, count the opens; more than
/// one is the finding, and the declaration the run sits on is what to name. That is cheap over a
/// tree the drift checks already walk, and it needs no compiler.
/// </summary>
public static class StackedSummaries
{
    /// <summary>The directories whose C# this is asked of.</summary>
    public static IReadOnlyList<string> Roots { get; } = ["app", "tests"];

    /// <summary>What a doc comment line starts with.</summary>
    public const string DocPrefix = "///";

    /// <summary>The element a member may carry exactly one of.</summary>
    public const string SummaryOpen = "<summary>";

    /// <summary>One root, or null outside a checkout.</summary>
    public static string? LocateRoot(string root) => SanitizerSource.LocateDirectory(root);

    /// <summary>
    /// The generated and built trees, which are neither this port's prose nor its to fix.
    /// </summary>
    public static bool IsOurs(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string normalised = path.Replace('/', '\\');

        return !normalised.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
            && !normalised.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every member in one file whose leading docstring carries more than one summary.
    /// </summary>
    /// <param name="source">The file's text.</param>
    /// <param name="where">Its path, for the report.</param>
    public static IReadOnlyList<StackedSummary> In(string source, string where)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(where);

        var found = new List<StackedSummary>();
        var summaries = 0;
        var runStart = 0;

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith(DocPrefix, StringComparison.Ordinal))
            {
                if (summaries == 0 && runStart == 0)
                    runStart = i + 1;

                summaries += Opens(trimmed);
                continue;
            }

            // The run ended. What it sits on is the first line that is not a doc comment - which for
            // a test is the attribute, and that is the right answer: the docstring belongs above it.
            if (runStart != 0)
            {
                if (summaries > 1)
                    found.Add(new StackedSummary(where, runStart, trimmed, summaries));

                summaries = 0;
                runStart = 0;
            }
        }

        return found;
    }

    /// <summary>How many summaries one line opens, which is normally none or one.</summary>
    private static int Opens(string line)
    {
        var count = 0;
        for (int at = line.IndexOf(SummaryOpen, StringComparison.Ordinal);
             at >= 0;
             at = line.IndexOf(SummaryOpen, at + SummaryOpen.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
