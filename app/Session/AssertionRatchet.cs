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
    /// The shipped tasks no assertion mentions, newest first - which is the order that matters,
    /// because the ones a rise is about are the ones shipped since the ceiling was last set.
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

        return [.. shipped.Where(id => !named.Contains(id)).OrderByDescending(NumberOf)];
    }

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
    [GeneratedRegex(@"^-\s+✅\s+\*\*(?<id>[A-Za-z]+[0-9]+)", RegexOptions.Multiline)]
    private static partial Regex ShippedRegex();

    // PP292, and not PP2 inside it.
    [GeneratedRegex(@"\bPP[0-9]+\b")]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"[0-9]+")]
    private static partial Regex NumberRegex();
}
