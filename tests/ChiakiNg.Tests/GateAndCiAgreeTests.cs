using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP433: the passes test.cmd runs and the passes CI runs.
///
/// Three locally, two in CI. The third is 454 runtime checks over the native seam, and PP117's hang
/// is why it is recorded here rather than added to build.yml from a machine that cannot test it.
/// </summary>
public class GateAndCiAgreeTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE TABLE IS WHAT THE TWO FILES DO.
    ///
    /// Both directions: a pass added to test.cmd is one CI should gain, and a pass CI stops running
    /// is one that turned local-only without anybody choosing that.
    /// </summary>
    [Fact]
    public void TheTableMatchesBothFiles()
    {
        if (GateAndCiAgree.ReadLocal() is not { } local)
            return;
        if (GateAndCiAgree.LocateCi() is not { } ci)
            return;

        foreach (TestPass pass in GateAndCiAgree.Passes)
            output.WriteLine($"{pass.Name}: local={pass.Locally} ci={pass.InCi}");

        IReadOnlyList<string> apart = GateAndCiAgree.Disagreements(
            local, File.ReadAllText(ci));

        Assert.True(
            apart.Count == 0,
            "the gate and CI no longer run what this table says they run:\n  "
                + string.Join("\n  ", apart));
    }

    /// <summary>
    /// AND ONE PASS IS LOCAL-ONLY, with its reason beside it.
    ///
    /// A difference with a reason is a decision; one without is an oversight. The ceiling is what
    /// stops a second appearing quietly.
    /// </summary>
    [Fact]
    public void OnlyTheSelftestIsLocalOnlyAndItSaysWhy()
    {
        IReadOnlyList<TestPass> localOnly = GateAndCiAgree.LocalOnly();

        // The ceiling is one, and it is named - so Single carries both halves.
        Assert.Equal(1, GateAndCiAgree.LocalOnlyCeiling);

        TestPass selftest = Assert.Single(localOnly);
        Assert.Contains("--selftest", selftest.Name, StringComparison.Ordinal);

        // The reason is the point, so it has to be one.
        Assert.True(
            selftest.Because.Length > 80,
            "the local-only pass carries no reason a reader could act on");
        Assert.Contains("PP117", selftest.Because, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every pass that CI does run is claimed to run locally too.
    ///
    /// The reverse gap would be worse and is not the one that exists: a pass CI runs and the gate
    /// does not is one a developer cannot reproduce before pushing.
    /// </summary>
    [Fact]
    public void NothingRunsInCiWithoutRunningLocally()
    {
        Assert.DoesNotContain(GateAndCiAgree.Passes, pass => pass.InCi && !pass.Locally);
    }

    /// <summary>
    /// A comment naming a command does not count as running it.
    ///
    /// build.yml's comments discuss test.cmd and ctest at length, and test.cmd's discuss the
    /// selftest before running it - so a reader counting comments would find every pass everywhere.
    /// test.cmd in fact runs NO ctest of its own: it launches scripts/test-windows.sh, which is why
    /// the local gate is read as both files.
    /// </summary>
    [Fact]
    public void ACommentNamingACommandIsNotRunningIt()
    {
        Assert.False(GateAndCiAgree.Runs(
            "rem the .NET host's --selftest is what PP75 gave a runner", "the host's --selftest"));

        Assert.False(GateAndCiAgree.Runs(
            "      # test.cmd is the local launcher for ctest and cannot be used here",
            "the C suite (ctest)"));

        // And the real thing is found.
        Assert.True(GateAndCiAgree.Runs("\"%APP_EXE%\" --selftest", "the host's --selftest"));
        Assert.True(GateAndCiAgree.Runs(
            "        run: ctest --test-dir build -C Release", "the C suite (ctest)"));
    }

    /// <summary>
    /// The selftest is genuinely absent from CI, asserted directly so the table cannot drift alone.
    /// </summary>
    [Fact]
    public void TheSelftestIsAbsentFromCi()
    {
        if (GateAndCiAgree.LocateCi() is not { } ci)
            return;
        if (GateAndCiAgree.ReadLocal() is not { } local)
            return;

        Assert.False(
            GateAndCiAgree.Runs(File.ReadAllText(ci), "the host's --selftest"),
            "CI runs the selftest now - update the table, and PP433 has been done");

        Assert.True(GateAndCiAgree.Runs(local, "the host's --selftest"));
    }

    /// <summary>PP272: and an empty file runs nothing.</summary>
    [Fact]
    public void AnEmptyFileRunsNothing()
    {
        foreach (TestPass pass in GateAndCiAgree.Passes)
            Assert.False(GateAndCiAgree.Runs("", pass.Name));

        // A pass this does not know is not silently claimed either.
        Assert.False(GateAndCiAgree.Runs("everything", "a pass nobody declared"));
    }
}
