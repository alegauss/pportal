using System.Globalization;

namespace ChiakiNg.Session;

/// <summary>One measured artifact against its ceiling.</summary>
/// <param name="Name">The budget's name, as the file spells it.</param>
/// <param name="Mib">What it measures now, in mebibytes.</param>
/// <param name="CeilingMib">What the file allows.</param>
public readonly record struct BudgetLine(string Name, double Mib, int CeilingMib)
{
    /// <summary>Whether it is inside its ceiling.</summary>
    public bool Holds => Mib <= CeilingMib;

    /// <summary>
    /// Whether the ceiling is owed a lowering - the artifact shrank and nobody moved it.
    ///
    /// A whole mebibyte of slack, so ordinary build noise does not demand an edit.
    /// </summary>
    public bool CeilingIsOwedALowering => CeilingMib - Mib >= 1.0;
}

/// <summary>
/// PP482, under PP303: how big this application is, as a ceiling rather than as a delta.
///
/// PP303 is an idea, and it asks a question it says is the author's to answer: PP46 measures cold start
/// and installer size against a Qt build carrying QtWebEngine, PP63's two multi-gigabyte installs exist
/// to produce that build, and PP277 has since settled that this is a new application rather than
/// upstream's next version - so the delta compares two products. Keep both, re-base PP46 on this
/// application alone, or retire both.
///
/// THIS DOES NOT ANSWER IT. What it does is remove one thing from the argument: the middle option said
/// "cold start and installer size as a budget with a ceiling, needing no Qt at all", and that half of
/// it turned out to be measurable today. The payload package.cmd stages and the installer it compresses
/// are both on disk after a package run, so the no-Qt option does not have to be built before it can be
/// weighed - only chosen.
///
/// THE CONTRACT IS THE ASSERTION RATCHET'S, deliberately. Each number may fall and may not rise, and a
/// fall owes a lowering in the same commit. PP38's file says why: a budget nobody lowers stops meaning
/// anything. So this reuses the shape rather than inventing a second one - the mistake PP454 and PP458
/// each cost a task to undo.
///
/// AND IT MEASURES WHAT package.cmd MADE, not what it should have made. The native tree it stages from
/// is whatever NATIVE_DIR held, in whatever configuration ran, because that is the artifact a user is
/// handed. Note that build\chiaki-ng-Win itself is NOT the measurement: it accumulates across builds -
/// a GUI-off build still finds Qt6 DLLs in it from an earlier GUI-on one - so measuring it would
/// measure build history. package.cmd selects from it, and the selection is the package.
/// </summary>
public static class PackageBudget
{
    /// <summary>The budget file, relative to the repository root.</summary>
    public const string BudgetRelativePath = @"tests\package-budget.txt";

    /// <summary>The staged payload the installer compresses.</summary>
    public const string PayloadRelativePath = @"build\chiaki-ng-package";

    /// <summary>And the installer itself.</summary>
    public const string InstallerRelativePath = @"build\chiaki-ng-windows-installer.exe";

    /// <summary>The two names the file carries.</summary>
    public const string PayloadBudget = "payload_mib";

    /// <summary>The installer's.</summary>
    public const string InstallerBudget = "installer_mib";

    /// <summary>The budget file, or null outside a checkout.</summary>
    public static string? LocateBudget() => SanitizerSource.LocateRelative(BudgetRelativePath);

    /// <summary>
    /// The ceilings the file declares, by name.
    ///
    /// Comments and blank lines are skipped; anything else must be `name value`, because a line this
    /// reader silently ignored would be a ceiling nobody was held to.
    /// </summary>
    public static IReadOnlyDictionary<string, int> CeilingsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ceilings = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                ceilings[parts[0]] = value;
            }
        }

        return ceilings;
    }

    /// <summary>
    /// How big a directory's contents are, in mebibytes, or null where it is not there.
    ///
    /// Null rather than zero: a package that has not been built is a different answer from one that is
    /// empty, and a budget test that read the first as the second would pass on nothing.
    /// </summary>
    public static double? MeasureDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!Directory.Exists(path))
            return null;

        long bytes = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);

        return bytes / (double)(1024 * 1024);
    }

    /// <summary>The same for one file.</summary>
    public static double? MeasureFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return File.Exists(path) ? new FileInfo(path).Length / (double)(1024 * 1024) : null;
    }

    /// <summary>
    /// Both budgets measured against their ceilings, or empty where the package has not been built.
    ///
    /// Empty is the honest answer on a machine that has only compiled: package.cmd is a separate step,
    /// and a budget cannot report on an artifact nobody made.
    /// </summary>
    public static IReadOnlyList<BudgetLine> Measure()
    {
        if (LocateBudget() is not { } budgetPath || SanitizerSource.RepositoryRoot() is not { } root)
            return [];

        IReadOnlyDictionary<string, int> ceilings = CeilingsIn(File.ReadAllText(budgetPath));
        var lines = new List<BudgetLine>();

        double? payload = MeasureDirectory(Path.Combine(root, PayloadRelativePath));
        if (payload is { } p && ceilings.TryGetValue(PayloadBudget, out int payloadCeiling))
            lines.Add(new BudgetLine(PayloadBudget, p, payloadCeiling));

        double? installer = MeasureFile(Path.Combine(root, InstallerRelativePath));
        if (installer is { } i && ceilings.TryGetValue(InstallerBudget, out int installerCeiling))
            lines.Add(new BudgetLine(InstallerBudget, i, installerCeiling));

        return lines;
    }
}
