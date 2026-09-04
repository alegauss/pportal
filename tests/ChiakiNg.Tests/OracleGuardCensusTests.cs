using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP665: PP663's cost, said out loud - how many assertions this configuration is not running.
///
/// Every comparison against a library PP33 deletes needs that library present, so each one asks
/// whether the shim carries the oracle and returns early when it does not. An early return in xUnit
/// is a PASS - so the suite prints the same total on a build with both oracles and on a build with
/// neither, and nothing in its output says which one ran.
///
/// PP683: and the count stopped at the test project. The host's selftest guards a comparison too,
/// so the number the gate printed was a floor bounded by a project boundary rather than by what a
/// bare build skips - and the one guarded comparison outside the census was the one whose guard
/// PP681 found to be wrong.
/// </summary>
public class OracleGuardCensusTests(ITestOutputHelper output)
{
    /// <summary>
    /// The gate says which configuration it ran under, in a number rather than in silent returns.
    ///
    /// This is the whole point of the file. It always passes and it always prints, because what it
    /// is for is the OUTPUT: a reader of a green run can see whether twenty-one comparisons ran or
    /// declined, which no other line of this suite tells them.
    ///
    /// PP56 is the precedent and the warning. There a stale binary made the suite report on code
    /// that had changed; here a build option makes it report on assertions that did not run. Both
    /// are a green worth less than a reader takes it for, and the answer to both is to say so.
    /// </summary>
    [Fact]
    public void TheGateSaysHowManyComparisonsThisBuildSkips()
    {
        bool holepunch = ShimHolepunchShape.OfTheBuild() == ShimShape.Wrapping;
        bool json = DeletedLibraryOracles.JsonOracleIsAvailable();

        output.WriteLine($"holepunch oracle: {holepunch}, json oracle: {json}");

        foreach ((GuardedFile file, int guards) in OracleGuardCensus.Counted())
            output.WriteLine($"  {guards,2} guard(s) in {file.Where} on {file.Guard}");

        int would = OracleGuardCensus.WouldDecline();
        int outside = OutsideTheTestProject();

        output.WriteLine(holepunch && json
            ? $"both oracles present: none of the {would} guarded assertions declined"
            : $"an oracle is absent: at least {would} assertions declined and were reported as passed");

        // PP683: and how much of that is the host's, which the number used to stop short of.
        output.WriteLine($"outside the test project: {outside}");

        Assert.True(would > 0, "no file guards on an oracle any more, so this census is about nothing");
    }

    /// <summary>What the census counts in files that are not part of the xUnit project.</summary>
    private static int OutsideTheTestProject()
        => OracleGuardCensus.Counted()
            .Where(one => !one.File.Where.StartsWith(@"tests\", StringComparison.Ordinal))
            .Sum(one => one.Guards);

    /// <summary>
    /// PP683: THE SELFTEST IS A ROW, and the printed total is larger for it.
    ///
    /// The census read test files only, and the host's 460 checks guard a comparison too - the
    /// device id's shape against holepunch.c's, declined on a build without the oracle exactly as
    /// the eleven decline. So the number the gate printed was a floor that stopped at a project
    /// boundary rather than at what a bare build actually skips.
    ///
    /// PP681 is why this is worth a row rather than a note. The one guarded comparison outside the
    /// census was the one whose guard was wrong - it read a header that declares the wrapper either
    /// way, so the branch below it never ran and the branch above it called an export that was not
    /// there. A census reaching the file would have had a row to be surprised by.
    ///
    /// Both halves asserted: the row is there and really guards, and the total is strictly bigger
    /// than the test project's own. A row added with a guard the file does not call would satisfy
    /// the first and fail the second.
    /// </summary>
    [Fact]
    public void TheSelftestIsCountedAndTheTotalRisesAboveTheTestProject()
    {
        if (OracleGuardCensus.Locate(OracleGuardCensus.SelfTestPath) is null)
            return;

        (GuardedFile File, int Guards) row = Assert.Single(
            OracleGuardCensus.Counted(),
            one => one.File.Where == OracleGuardCensus.SelfTestPath);

        Assert.Equal(OracleGuardCensus.SelfTestGuard, row.File.Guard);
        Assert.True(row.Guards > 0, "the selftest no longer asks the guard this row names");

        int inTests = OracleGuardCensus.Counted()
            .Where(one => one.File.Where.StartsWith(@"tests\", StringComparison.Ordinal))
            .Sum(one => one.Guards);

        output.WriteLine($"{inTests} in the test project, {OracleGuardCensus.WouldDecline()} in all");

        Assert.True(
            OracleGuardCensus.WouldDecline() > inTests,
            "the total still stops at the test project, so the host's guards cost nothing visible");
    }

    /// <summary>
    /// And the guard the row names is the one the selftest actually asks, read out of the file.
    ///
    /// The row could name a predicate that exists and is never called there, which the count above
    /// would report as zero and this says out loud: the comparison it protects is the device id's,
    /// and it is asked once.
    /// </summary>
    [Fact]
    public void TheSelftestAsksThatGuardBeforeItsOneComparison()
    {
        if (OracleGuardCensus.Locate(OracleGuardCensus.SelfTestPath) is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(1, OracleGuardCensus.GuardsIn(source, OracleGuardCensus.SelfTestGuard));

        Assert.Contains(
            $"if ({OracleGuardCensus.SelfTestGuard}())", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every file this names really does guard, which is what stops the number being a wish.
    ///
    /// A file that stopped guarding is a finding rather than a smaller total: it means an assertion
    /// somewhere will throw instead of declining the next time the flip's option is off.
    /// </summary>
    [Fact]
    public void EveryFileNamedHereStillGuards()
    {
        IReadOnlyList<(GuardedFile File, int Guards)> counted = OracleGuardCensus.Counted();
        if (counted.Count == 0)
            return;

        foreach ((GuardedFile file, int guards) in counted)
        {
            Assert.True(
                guards > 0,
                $"{file.Where} no longer calls {file.Guard}, so its assertions will throw rather "
                    + "than decline on a build without the oracle");
        }

        Assert.Equal(OracleGuardCensus.Files.Count, counted.Count);
    }

    /// <summary>
    /// The counter counts the name before a parenthesis, and PROSE naming it is not that.
    ///
    /// A definition counts, which is why the total is a floor and not an exact figure: one of the
    /// five files defines its own guard as a one-line helper, so its number is the tests plus that.
    /// Precision was never the point - the point is that the gate says something rather than
    /// nothing, and a floor says it.
    ///
    /// What must not count is a comment or a docstring naming the guard, because those appear
    /// wherever the mechanism is explained and would make the number about the prose.
    /// </summary>
    [Theory]
    [InlineData("if (!DeletedLibraryOracles.JsonOracleIsAvailable())", 1)]
    [InlineData("public static bool JsonOracleIsAvailable()\n{ return true; }", 1)]
    [InlineData("// JsonOracleIsAvailable is what this asks", 0)]
    [InlineData("", 0)]
    public void CallsAreCountedAndProseIsNot(string source, int expected)
        => Assert.Equal(expected, OracleGuardCensus.GuardsIn(source, "JsonOracleIsAvailable"));
}
