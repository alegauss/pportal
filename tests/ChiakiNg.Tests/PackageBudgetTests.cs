using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP482, PP303: the application's own size, as a ceiling rather than a delta.
///
/// PP303 asks whether PP46 still earns PP63's two multi-gigabyte installs, and says the answer is the
/// author's. This does not answer it - it removes one thing from the argument, by showing that the
/// no-Qt option PP303 lists second is measurable today rather than something to be built first.
///
/// The contract is PP38's ratchet: each number may fall and may not rise, and a fall owes a lowering.
/// </summary>
public class PackageBudgetTests
{
    /// <summary>The file declares both budgets and nothing else.</summary>
    [Fact]
    public void TheFileDeclaresBothBudgets()
    {
        if (PackageBudget.LocateBudget() is not { } path)
            return;

        IReadOnlyDictionary<string, int> ceilings =
            PackageBudget.CeilingsIn(File.ReadAllText(path));

        Assert.Equal(2, ceilings.Count);
        Assert.True(ceilings.ContainsKey(PackageBudget.PayloadBudget));
        Assert.True(ceilings.ContainsKey(PackageBudget.InstallerBudget));

        // Both positive, because a ceiling of zero would pass for an artifact nobody built.
        Assert.All(ceilings.Values, v => Assert.True(v > 0));
    }

    /// <summary>
    /// THE GATE: every budget is inside its ceiling, and no ceiling is owed a lowering.
    ///
    /// Both directions, which is what makes it a ratchet rather than a limit. Skips where package.cmd
    /// has not run - that is a separate step, and a budget cannot report on an artifact nobody made.
    /// </summary>
    [Fact]
    public void EveryBudgetHoldsAndNoCeilingIsOwedALowering()
    {
        IReadOnlyList<BudgetLine> lines = PackageBudget.Measure();
        if (lines.Count == 0)
            return;

        foreach (BudgetLine line in lines)
        {
            Assert.True(
                line.Holds,
                $"{line.Name} is {line.Mib:F1} MiB against a ceiling of {line.CeilingMib}: either the "
                    + "package grew for a reason worth arguing in this commit, or it grew by accident");

            Assert.False(
                line.CeilingIsOwedALowering,
                $"{line.Name} is {line.Mib:F1} MiB against a ceiling of {line.CeilingMib}: it shrank, so "
                    + "lower the ceiling in the same commit - a budget nobody lowers stops meaning anything");
        }
    }

    /// <summary>
    /// A missing artifact is null, not zero - so the gate above skips rather than passing on nothing.
    /// </summary>
    [Fact]
    public void AMissingArtifactIsNullRatherThanZero()
    {
        Assert.Null(PackageBudget.MeasureDirectory(Path.Combine(Path.GetTempPath(), "pp482-absent")));
        Assert.Null(PackageBudget.MeasureFile(Path.Combine(Path.GetTempPath(), "pp482-absent.exe")));
    }

    /// <summary>
    /// The reader skips comments and blank lines, and refuses a line it cannot parse rather than
    /// ignoring it quietly.
    /// </summary>
    [Fact]
    public void TheReaderTakesOnlyNameAndNumber()
    {
        const string text = """
            # a comment
            payload_mib 200

            installer_mib 95
            not_a_number abc
            """;

        IReadOnlyDictionary<string, int> ceilings = PackageBudget.CeilingsIn(text);

        Assert.Equal(2, ceilings.Count);
        Assert.Equal(200, ceilings["payload_mib"]);
        Assert.Equal(95, ceilings["installer_mib"]);
        Assert.False(ceilings.ContainsKey("not_a_number"));
    }

    /// <summary>
    /// And the measurement is NOT the native tree, which accumulates across builds.
    ///
    /// build\chiaki-ng-Win holds Qt6 DLLs from an earlier GUI-on build even when the current one had
    /// CHIAKI_ENABLE_GUI off, so measuring it would measure build history. package.cmd selects from it,
    /// and the selection is the package.
    /// </summary>
    [Fact]
    public void TheBudgetMeasuresThePackageAndNotTheNativeTree()
    {
        Assert.Contains("chiaki-ng-package", PackageBudget.PayloadRelativePath);
        Assert.DoesNotContain("chiaki-ng-Win", PackageBudget.PayloadRelativePath);
        Assert.Contains("windows-installer", PackageBudget.InstallerRelativePath);
    }

    /// <summary>PP272: and the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptyFileDeclaresNothing()
    {
        Assert.Empty(PackageBudget.CeilingsIn(""));
        Assert.Empty(PackageBudget.CeilingsIn("# only a comment\n"));
    }
}
