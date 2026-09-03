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
        output.WriteLine(holepunch && json
            ? $"both oracles present: none of the {would} guarded assertions declined"
            : $"an oracle is absent: at least {would} assertions declined and were reported as passed");

        Assert.True(would > 0, "no file guards on an oracle any more, so this census is about nothing");
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
