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

        if (GateAndCiAgree.ReadCi() is { } ci)
            Assert.True(GateAndCiAgree.Runs(ci, pass.Name));
    }

    /// <summary>
    /// PP569: and the hyphen tells the two selftests apart, which is the whole of the distinction.
    /// A matcher that confused them would report the host's pass as the tool's and hide the gap
    /// this closed.
    /// </summary>
    [Fact]
    public void TheTwoSelftestsAreNotTheSameFlag()
    {
        Assert.True(GateAndCiAgree.Runs("compare-baselines --self-test", "the tool's --self-test"));
        Assert.False(GateAndCiAgree.Runs("compare-baselines --self-test", "the host's --selftest"));

        Assert.True(GateAndCiAgree.Runs("run --selftest here", "the host's --selftest"));
        Assert.False(GateAndCiAgree.Runs("run --selftest here", "the tool's --self-test"));
    }

    /// <summary>
    /// PP570: THE SECOND TOOL, which PP569's sweep stopped short of.
    ///
    /// measure-startup ships the same flag and was not in the solution at all, so no gate built it,
    /// let alone ran it - worse than the gap PP569 closed, where the project was at least compiled.
    /// </summary>
    [Fact]
    public void MeasureStartupsSelfTestIsRunByBoth()
    {
        TestPass pass = Assert.Single(
            GateAndCiAgree.Passes, one => one.Name == "measure-startup's self-test");

        Assert.True(pass.Locally);
        Assert.True(pass.InCi);
        Assert.Empty(pass.Because);

        if (GateAndCiAgree.ReadLocal() is { } local)
            Assert.True(GateAndCiAgree.Runs(local, pass.Name));

        if (GateAndCiAgree.ReadCi() is { } ci)
            Assert.True(GateAndCiAgree.Runs(ci, pass.Name));
    }

    /// <summary>
    /// PP570: and the two tools are told apart by their binaries, not by the flag they share.
    ///
    /// Matching on the flag alone was the defect: wiring one tool satisfied the check for both,
    /// which is exactly the green that let measure-startup stay unwired through PP569.
    /// </summary>
    [Fact]
    public void OneToolWiredDoesNotSatisfyTheOther()
    {
        const string onlyOne = "compare-baselines --self-test";

        Assert.True(GateAndCiAgree.Runs(onlyOne, "the tool's --self-test"));
        Assert.False(GateAndCiAgree.Runs(onlyOne, "measure-startup's self-test"));
    }

    [Fact]
    public void TheTableMatchesBothFiles()
    {
        if (GateAndCiAgree.ReadLocal() is not { } local)
            return;
        if (GateAndCiAgree.ReadCi() is not { } ci)
            return;

        foreach (TestPass pass in GateAndCiAgree.Passes)
            output.WriteLine($"{pass.Name}: local={pass.Locally} ci={pass.InCi}");

        IReadOnlyList<string> apart = GateAndCiAgree.Disagreements(
            local, ci);

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
    /// PP587: the pass that arrived from the CI side, run by both now.
    ///
    /// It is the mirror image of the host's selftest, and the direction
    /// <see cref="NothingRunsInCiWithoutRunningLocally"/> already refused: a pass CI runs and the
    /// gate does not is one a developer cannot reproduce before pushing. That assertion was green
    /// only because this pass was not in the table - the workflow existed, and nothing here read it.
    /// </summary>
    [Fact]
    public void RoadkeepLintIsRunByBoth()
    {
        TestPass pass = Assert.Single(GateAndCiAgree.Passes, one => one.Name == "roadkeep lint");

        Assert.True(pass.Locally);
        Assert.True(pass.InCi);
        Assert.Empty(pass.Because);

        if (GateAndCiAgree.ReadLocal() is { } local)
            Assert.True(GateAndCiAgree.Runs(local, pass.Name));

        if (GateAndCiAgree.ReadCi() is { } ci)
            Assert.True(GateAndCiAgree.Runs(ci, pass.Name));
    }

    /// <summary>
    /// PP587: CI is three workflows, and the pass that made that matter is in the second of them.
    ///
    /// Reading build.yml alone would report CI as not running roadkeep lint, and the disagreement
    /// would be in the reader rather than in the tree - the failure this whole class exists to
    /// avoid, one level up from the passes it compares.
    /// </summary>
    [Fact]
    public void TheCiSideIsEveryWorkflowAndNotJustTheBuild()
    {
        Assert.Equal(3, GateAndCiAgree.CiRelativePaths.Count);
        Assert.Contains(@".github\workflows\roadkeep.yml", GateAndCiAgree.CiRelativePaths);

        // The build workflow alone does NOT run it, which is why the list had to widen.
        string? build = SanitizerSource.LocateRelative(@".github\workflows\build.yml");
        if (build is null)
            return;

        Assert.False(GateAndCiAgree.Runs(File.ReadAllText(build), "roadkeep lint"));
    }

    /// <summary>
    /// PP587: the two spellings, and neither is the other's substring.
    ///
    /// The gate calls the verb and CI uses the published action, which roadkeep.yml's own comment
    /// says is deliberate - a copied run block drifts per repository. A matcher holding only the
    /// verb would have reported CI as not running the pass it does run.
    /// </summary>
    [Fact]
    public void TheVerbAndThePublishedActionBothCount()
    {
        Assert.True(GateAndCiAgree.Runs("    roadkeep lint", "roadkeep lint"));
        Assert.True(GateAndCiAgree.Runs("      - uses: alegauss/roadkeep@main", "roadkeep lint"));

        // And neither spelling answers for another pass.
        Assert.False(GateAndCiAgree.Runs("      - uses: alegauss/roadkeep@main", "the C suite (ctest)"));
        Assert.False(GateAndCiAgree.Runs("    roadkeep lint", "the host's --selftest"));

        // A comment about it is still not running it.
        Assert.False(GateAndCiAgree.Runs(
            "rem `roadkeep lint` exits 1 on a governed file that drifted", "roadkeep lint"));
    }

    /// <summary>
    /// PP587: AND NEITHER IS THE BANNER. The gate echoes "[test] roadkeep lint" one line above the
    /// call, so a check that only asked whether the file CONTAINS the verb was satisfied by the
    /// label - green with the call deleted, which is how this was found.
    ///
    /// A comment is not the only line that can name a command without running it, which is the half
    /// <see cref="ACommentNamingACommandIsNotRunningIt"/> does not cover.
    /// </summary>
    [Fact]
    public void AnEchoOfTheStepIsNotRunningIt()
    {
        const string bannerOnly = """
            echo.
            echo [test] roadkeep lint
            """;

        Assert.False(GateAndCiAgree.Runs(bannerOnly, "roadkeep lint"));

        // The banner AND the call is what the gate actually has, and that runs it.
        Assert.True(GateAndCiAgree.Runs(bannerOnly + "\n    roadkeep lint", "roadkeep lint"));

        // Invokes is the rule underneath, and it is about where the command sits on the line.
        Assert.True(GateAndCiAgree.Invokes("  roadkeep lint\n", "roadkeep lint"));
        Assert.False(GateAndCiAgree.Invokes("echo roadkeep lint\n", "roadkeep lint"));
    }

    /// <summary>
    /// PP588: THE TWO HALVES HAVE TO REACH EACH OTHER, which is what two Contains never asked.
    ///
    /// The gate announces each tool before running it and runs the two one after the other, so the
    /// banner supplied the binary and the neighbour's call supplied the flag. Either invocation
    /// could be deleted and its pass stayed green - PP570's own defect, one step along from the
    /// split by binary that was supposed to close it.
    /// </summary>
    [Fact]
    public void ABannerPlusTheOtherToolsFlagIsNotAnInvocation()
    {
        // Exactly the shape the gate had: compare-baselines is only ever echoed here, and the flag
        // belongs to the call below it.
        const string neighbourOnly = """
            echo [test] compare-baselines selftest
            set "MS_EXE=%~dp0tools\measure-startup\bin\Debug\measure-startup.exe"
            "%MS_EXE%" --self-test
            """;

        Assert.False(GateAndCiAgree.RunsTool(neighbourOnly, "compare-baselines", "--self-test"));

        // And the tool that IS wired is still found, through its variable.
        Assert.True(GateAndCiAgree.RunsTool(neighbourOnly, "measure-startup", "--self-test"));
    }

    /// <summary>
    /// PP588: both spellings of a real call count - one line in CI, a variable in the gate.
    ///
    /// A rule that took only the one-line form would report the gate as not running what it runs,
    /// which is the failure in the opposite direction and just as wrong.
    /// </summary>
    [Fact]
    public void AToolRunsThroughOneLineOrThroughItsVariable()
    {
        Assert.True(GateAndCiAgree.RunsTool(
            "dotnet run --project tools/compare-baselines/CompareBaselines.csproj -- --self-test",
            "compare-baselines",
            "--self-test"));

        Assert.True(GateAndCiAgree.RunsTool(
            "set \"CB_EXE=%~dp0tools\\compare-baselines\\bin\\compare-baselines.exe\"\n"
                + "\"%CB_EXE%\" --self-test",
            "compare-baselines",
            "--self-test"));

        // The variable set to a DIFFERENT tool does not carry the flag across.
        Assert.False(GateAndCiAgree.RunsTool(
            "set \"CB_EXE=%~dp0tools\\compare-baselines\\bin\\compare-baselines.exe\"\n"
                + "\"%CB_EXE%\" --self-test",
            "measure-startup",
            "--self-test"));
    }

    /// <summary>
    /// PP588: and the pair is still not satisfied by the two words being anywhere in the file.
    ///
    /// This is the case the old rule passed and the new one refuses, stated on its own so the
    /// distinction survives a later edit to either matcher.
    /// </summary>
    [Fact]
    public void TheBinaryAndTheFlagOnUnrelatedLinesAreNotACall()
    {
        const string apart = """
            set "CB_EXE=%~dp0tools\compare-baselines\bin\compare-baselines.exe"
            "%OTHER_EXE%" --self-test
            """;

        Assert.False(GateAndCiAgree.RunsTool(apart, "compare-baselines", "--self-test"));
    }

    /// <summary>
    /// PP589: a temp file called ctest_out is not the C suite running.
    ///
    /// This was the last pass on a bare Contains, and the most important one - PP439 already found
    /// that ctest reports the whole suite as a single test, so a suite can vanish inside a green.
    /// Nine non-comment lines carried the string and none of them was the call, which runs the tool
    /// through "$CTEST" because ctest is not on a plain Windows PATH (PP67).
    /// </summary>
    [Fact]
    public void ATempFileNamedForTheToolIsNotTheToolRunning()
    {
        // The script with its invocation removed, which is exactly what was measured. Every line
        // here is one that was answering for the call, INCLUDING the two the first version of this
        // fix still fell for: the warning echo, and `cases=$(… "$ctest_out" …)`, whose value holds
        // the letters of ctest inside a longer name and made `"$cases"` read as running the tool.
        const string withoutTheCall = """
            CTEST="${CTEST:-/mingw64/bin/ctest}"
            ctest_out=$(mktemp)
            cat "$ctest_out"
            rm -f "$ctest_out"
            grep -E '^[0-9]+% tests passed' "$ctest_out"
            cases=$(sed -nE 's/^1: ([0-9]+).*/\1/p' "$ctest_out" | tail -1)
            if [[ -z "$cases" ]]; then
            echo "[test] WARNING: could not read the case count from ctest -V output." >&2
            fi
            """;

        Assert.False(GateAndCiAgree.RunsCommand(withoutTheCall, "ctest"));

        // And with it back, through the variable the gate actually uses.
        const string withTheCall = withoutTheCall
            + "\ntimeout \"$TEST_TIMEOUT\" \"$CTEST\" --test-dir \"$BUILD_DIR\" -V >\"$ctest_out\" 2>&1";

        Assert.True(GateAndCiAgree.RunsCommand(withTheCall, "ctest"));
    }

    /// <summary>
    /// PP589: and CI's spelling, which names the command outright rather than through a path.
    ///
    /// Both forms are real invocations. A rule holding only the variable one would report CI as not
    /// running the suite, which is the mirror error and just as wrong.
    /// </summary>
    [Fact]
    public void TheCommandCountsByNameOrThroughItsVariable()
    {
        Assert.True(GateAndCiAgree.RunsCommand(
            "run: ctest --test-dir build -C Release --output-on-failure", "ctest"));

        // An assignment is not a call, even when its value is the tool's own path.
        Assert.False(GateAndCiAgree.RunsCommand("CTEST=\"${CTEST:-/mingw64/bin/ctest}\"", "ctest"));

        // Nor is a banner.
        Assert.False(GateAndCiAgree.RunsCommand(
            "echo \"[test] could not read the case count from ctest -V output.\"", "ctest"));
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
        if (GateAndCiAgree.ReadCi() is not { } ci)
            return;
        if (GateAndCiAgree.ReadLocal() is not { } local)
            return;

        Assert.False(
            GateAndCiAgree.Runs(ci, "the host's --selftest"),
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
