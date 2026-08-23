using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP38: the non-goal, counted.
///
/// "No line ships without an assertion that fails without it" is a sentence in a file, and the
/// first week under pressure is when it stops being true. What makes it hold is a number in CI that
/// may fall and may not rise - which does not demand the debt be paid at once, only that it stop
/// growing, and that is what makes it survivable.
///
/// THE JOIN, which was the one piece of design in this task
/// --------------------------------------------------------
/// A count is a guess unless the ledger and the suites can be joined, and a gate on a guess is
/// worse than no gate. This port already had the join and had not noticed: every assertion here
/// names the task it holds, in its own summary - "PP292, now fixed on both sides", "PP36: a red
/// assertion has somewhere to stop something" - because that is how this tree has been written from
/// the start. So the join is the id, and it needs no attribute and no naming convention imposed on
/// top of what is there.
///
/// It is a coarse join and that is stated rather than hidden. An id mentioned anywhere in an
/// assertion file counts, so a test naming PP292 in passing covers it as far as this is concerned.
/// The alternative - proving that some assertion would fail without some line - is the halting
/// problem with extra steps. What this catches is the case the non-goal is actually about: a task
/// shipped with no test mentioning it at all, which is a task shipped with no test.
///
/// WHERE AN ASSERTION LIVES
/// ------------------------
/// Three places, and app\SelfTest.cs is the one that is easy to leave out. It is not under a test
/// directory and it is the host's own assembly, but `test.cmd` runs it and it is where the
/// packaging, drift and resolver checks assert - fourteen shipped tasks read as uncovered without
/// it. Nothing else under app\ is scanned: an id in production code is a comment, not a check.
/// </summary>
public static partial class AssertionRatchet
{
    /// <summary>The ledger, which is the only file that says a task shipped.</summary>
    public const string LedgerRelativePath = @"docs\CHANGELOG.md";

    /// <summary>Where the ceiling is kept. Not governed: it is a measurement, not a plan.</summary>
    public const string CeilingRelativePath = @"tests\assertion-ratchet.txt";

    /// <summary>
    /// The trees an assertion can live in, and the one file outside them that is also assertions.
    /// </summary>
    public static IReadOnlyList<string> AssertionPaths { get; } =
        ["tests", "test", @"app\SelfTest.cs"];

    /// <summary>Extensions worth reading in those trees.</summary>
    public static IReadOnlySet<string> AssertionExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".c", ".h", ".inl" };

    /// <summary>The ledger, or null outside a checkout.</summary>
    public static string? LocateLedger() => SanitizerSource.LocateRelative(LedgerRelativePath);

    /// <summary>The ceiling file, or null outside a checkout.</summary>
    public static string? LocateCeiling() => SanitizerSource.LocateRelative(CeilingRelativePath);

    /// <summary>
    /// Every task the ledger records as shipped.
    ///
    /// Only the shipped marker, and deliberately: a retired line is work that was abandoned, and
    /// demanding a test for something nobody built would make the number grow by not doing things.
    /// A partial - "PP22 (the single-file publish)" - is the id alone, because the ledger holds one
    /// entry per half and the task is one task.
    /// </summary>
    public static IReadOnlySet<string> Shipped(string ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match entry in ShippedRegex().Matches(ledger))
            ids.Add(entry.Groups["id"].Value);

        return ids;
    }

    /// <summary>
    /// PP305: each shipped task with the sentence the ledger gives it.
    ///
    /// A list of uncovered ids cannot be paid down. Ninety-seven bare ids say nothing about where an
    /// assertion for one would go, or whether one already exists under a neighbouring id - which is
    /// the case this is for. PP300's parser, ladder and packets are all checked, in a file whose
    /// summary names PP29 because that is the task it was written under. Beside its symptom that is
    /// obvious in a second; as the string "PP300" it is a morning.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ShippedWithSymptom(string ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match entry in ShippedRegex().Matches(ledger))
        {
            // First wins. A partial and its completion are two entries for one task, and the first
            // is the one stating the problem rather than the half that landed.
            string id = entry.Groups["id"].Value;
            entries.TryAdd(id, entry.Groups["symptom"].Value.Trim());
        }

        return entries;
    }

    /// <summary>
    /// PP311: which assertion files name an id, and on which line.
    ///
    /// The join cannot be repaired by parsing - measured, and the reason is that this tree writes
    /// some of its coverage claims AS data: SuiteCoverageTests is a table of which class covers
    /// which C file, so the ids in its string literals are claims and the ids in a fixture next
    /// door are not. Nothing about the syntax tells them apart.
    ///
    /// So the answer is not prevention but audit. Three times in one commit an id written as test
    /// data paid a real task's debt, and each time the only symptom was the count falling by one
    /// more than expected - which is a question this answers in one command instead of a grep.
    /// </summary>
    public static IReadOnlyList<string> WhereNamed(string root, string id)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(id);

        var found = new List<string>();

        foreach (string file in AssertionFiles(root))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!Named(lines[i]).Contains(id))
                    continue;

                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                found.Add($"{relative}:{i + 1}  {lines[i].Trim()}");
            }
        }

        return found;
    }

    /// <summary>
    /// PP308: whether a repository-relative path is one an assertion could live in.
    ///
    /// The same three places <see cref="AssertionPaths"/> names, asked of a path rather than found
    /// by walking - which is what lets a list of files a COMMIT touched be filtered the same way as
    /// a checkout.
    /// </summary>
    public static bool IsAssertionPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string normalised = path.Trim().Replace('\\', '/');
        if (normalised.Length == 0 || !AssertionExtensions.Contains(Path.GetExtension(normalised)))
            return false;

        return normalised.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
            || normalised.StartsWith("test/", StringComparison.OrdinalIgnoreCase)
            || normalised.Equals("app/SelfTest.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PP308: the assertion files among a set of changed paths, in order and without repeats.
    ///
    /// THE DECISION THIS TASK WAS FILED TO TAKE, and it is not the one the symptom proposed.
    ///
    /// Asking git whether the commit that shipped a task also touched an assertion file answers 75
    /// of the 96 the id join misses, and is the non-goal restated - the rule says a test lands in
    /// the same commit as the line it holds, which is a fact about a commit. It is tempting as the
    /// gate and it is wrong as the gate. actions/checkout takes depth 1, so every task would read
    /// as uncovered on a runner until the workflow downloads the whole history on every job; the
    /// test would shell out to a program a unit test has no business needing; and nine ids shipped
    /// under a scope that is not their own, so the id join has to stay anyway.
    ///
    /// So git is a DIAGNOSTIC and not a gate. The count stays the id join - free, offline, and
    /// wrong in the direction that only ever asks for more tests. This runs where a person is
    /// paying the debt down, on a machine that has the history, and answers the one question that
    /// makes paying cheap: the assertions for this task already exist, and here is the file.
    /// </summary>
    public static IReadOnlyList<string> AssertionFilesIn(IEnumerable<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);

        var files = new List<string>();

        foreach (string path in changedPaths)
        {
            string normalised = path.Trim().Replace('\\', '/');
            if (IsAssertionPath(normalised) && !files.Contains(normalised, StringComparer.OrdinalIgnoreCase))
                files.Add(normalised);
        }

        return files;
    }

    /// <summary>Every task id named anywhere in a body of assertion text.</summary>
    public static IReadOnlySet<string> Named(string assertions)
    {
        ArgumentNullException.ThrowIfNull(assertions);

        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match id in IdRegex().Matches(assertions))
            ids.Add(id.Value);

        return ids;
    }

    /// <summary>Every file an assertion could be written in, under this checkout.</summary>
    public static IReadOnlyList<string> AssertionFiles(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var files = new List<string>();

        foreach (string relative in AssertionPaths)
        {
            string path = Path.Combine(root, relative);

            if (File.Exists(path))
            {
                files.Add(path);
                continue;
            }

            if (!Directory.Exists(path))
                continue;

            files.AddRange(Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(p => AssertionExtensions.Contains(Path.GetExtension(p)))
                .Where(NotBuilt));
        }

        return files;
    }

    /// <summary>
    /// The shipped tasks no assertion mentions and no exemption excuses, newest first - which is
    /// the order that matters, because a rise is about what shipped since the ceiling was set.
    /// </summary>
    public static IReadOnlyList<string> Uncovered(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string? ledgerPath = LocateLedgerFrom(root);
        if (ledgerPath is null)
            return [];

        IReadOnlySet<string> shipped = Shipped(File.ReadAllText(ledgerPath));

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in AssertionFiles(root))
            named.UnionWith(Named(File.ReadAllText(file)));

        IReadOnlyDictionary<string, string> exempt = Exemptions(root);

        return
        [
            .. shipped
                .Where(id => !named.Contains(id) && !exempt.ContainsKey(id))
                .OrderByDescending(NumberOf),
        ];
    }

    /// <summary>
    /// PP310: the tasks no assertion can cover, each with the reason, read from the ceiling file.
    ///
    /// The door that is not raising the ceiling. A task whose output is prose - a pass over a list,
    /// a measurement, a change to the gates themselves - has nothing to assert, so under PP38 as
    /// shipped it could not be shipped at all: the count rose and the ceiling may not. PP307 hit
    /// that one commit after PP38, and eleven of the twelve tasks its own pass examined are the
    /// same shape, so this is the ordinary case rather than an edge.
    ///
    /// Raising the ceiling would have been the wrong door. Its whole value is that the number
    /// cannot go up, and a project that raises it once for a good reason raises it later for a
    /// worse one, in a commit about something else. An exemption is not silent: it is a line a
    /// person wrote, beside the number, read in every diff that touches the file.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Exemptions(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string path = Path.Combine(root, CeilingRelativePath);
        return File.Exists(path) ? ExemptionsIn(File.ReadAllText(path)) : ReadOnlyEmpty;
    }

    /// <summary>
    /// The exemptions in a ceiling file: <c>exempt PP307 - the reason</c>, one per line.
    ///
    /// A line with no reason after the id is NOT an exemption and is not read as one. That is the
    /// difference between a record and a loophole: a bare list of ids is something somebody appends
    /// to in a hurry, and a list of sentences is something a reviewer reads.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExemptionsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var exempt = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match line in ExemptionRegex().Matches(text))
        {
            string reason = line.Groups["reason"].Value.Trim(' ', '\t', '\r', '-', '—');
            if (reason.Length > 0)
                exempt.TryAdd(line.Groups["id"].Value, reason);
        }

        return exempt;
    }

    private static readonly Dictionary<string, string> ReadOnlyEmpty = [];

    /// <summary>The ceiling on disk, or -1 where there is none to read.</summary>
    public static int Ceiling(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string path = Path.Combine(root, CeilingRelativePath);
        if (!File.Exists(path))
            return -1;

        return CeilingIn(File.ReadAllText(path));
    }

    /// <summary>
    /// The number in a ceiling file, or -1 where it carries none.
    ///
    /// Comment lines are allowed and are the reason this is not int.Parse on the whole file: the
    /// number alone tells whoever finds it nothing about what it is for, and a ratchet nobody
    /// understands is a ratchet somebody raises.
    /// </summary>
    public static int CeilingIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;

            return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int ceiling)
                ? ceiling
                : -1;
        }

        return -1;
    }

    /// <summary>The numeric part of an id, for ordering. Ids are PP-prefixed and one family.</summary>
    public static int NumberOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        Match number = NumberRegex().Match(id);
        return number.Success
            ? int.Parse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
    }

    private static string? LocateLedgerFrom(string root)
    {
        string path = Path.Combine(root, LedgerRelativePath);
        return File.Exists(path) ? path : null;
    }

    private static bool NotBuilt(string path)
    {
        char sep = Path.DirectorySeparatorChar;
        return !path.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
            && !path.Contains($"{sep}obj{sep}", StringComparison.Ordinal);
    }

    // - ✅ **PP22 (the single-file publish)** **symptom** — outcome.
    //
    // The symptom is the SECOND bold run, which is why the first is matched lazily up to its close:
    // a partial's id carries a qualifier inside the same asterisks, and taking the greedy reading
    // would swallow the sentence this exists to keep.
    [GeneratedRegex(@"^-\s+✅\s+\*\*(?<id>[A-Za-z]+[0-9]+)[^*]*\*\*\s+\*\*(?<symptom>.+?)\*\*", RegexOptions.Multiline)]
    private static partial Regex ShippedRegex();

    // PP292, and not PP2 inside it.
    [GeneratedRegex(@"\bPP[0-9]+\b")]
    private static partial Regex IdRegex();

    // # exempt PP307 - a pass over a list, whose whole output is the prose above.
    //
    // The comment marker is optional and the reason is not. Every other line in that file is a
    // comment, so requiring one here would be a trap, and allowing a bare id would make this the
    // loophole it exists instead of.
    [GeneratedRegex(
        @"^\s*#?\s*exempt\s+(?<id>[A-Za-z]+[0-9]+)(?<reason>[^\r\n]*)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ExemptionRegex();

    [GeneratedRegex(@"[0-9]+")]
    private static partial Regex NumberRegex();
}
