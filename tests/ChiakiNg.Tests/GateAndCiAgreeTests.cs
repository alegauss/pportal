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
    /// <summary>
    /// PP569: the tool's own assertion, which NEITHER side ran.
    ///
    /// compare-baselines ships `--self-test` and its README calls it "the assertion this tool ships
    /// with". The tool is in the solution, so the gate and CI both BUILT it and neither executed it -
    /// a green build over an assertion nobody runs, which is the lie PP56, PP74 and PP75 are about.
    /// It matters more than a spare tool would: the site's front page promises what this prints.
    ///
    /// Asserted on both sides, because half the fix is how it got missed the first time.
    /// </summary>
    [Fact]
    public void TheToolsSelfTestIsRunByBoth()
    {
        TestPass pass = Assert.Single(
            GateAndCiAgree.Passes, one => one.Name == "the tool's --self-test");

        Assert.True(pass.Locally);
        Assert.True(pass.InCi);

        // Run by both, so it needs no reason - a difference with a reason is a decision, and this
        // is not a difference.
        Assert.Empty(pass.Because);

        if (GateAndCiAgree.ReadLocal() is { } local)
            Assert.True(GateAndCiAgree.Runs(local, pass.Name));

        if (GateAndCiAgree.LocateCi() is { } ci)
            Assert.True(GateAndCiAgree.Runs(File.ReadAllText(ci), pass.Name));
    }

    /// <summary>
    /// PP569: and the hyphen tells the two selftests apart, which is the whole of the distinction.
    /// A matcher that confused them would report the host's pass as the tool's and hide the gap
    /// this closed.
    /// </summary>
    [Fact]
    public void TheTwoSelftestsAreNotTheSameFlag()
    {
        Assert.True(GateAndCiAgree.Runs("run --self-test here", "the tool's --self-test"));
        Assert.False(GateAndCiAgree.Runs("run --self-test here", "the host's --selftest"));

        Assert.True(GateAndCiAgree.Runs("run --selftest here", "the host's --selftest"));
        Assert.False(GateAndCiAgree.Runs("run --selftest here", "the tool's --self-test"));
    }

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
