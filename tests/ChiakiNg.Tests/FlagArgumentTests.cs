using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP329: the value a flag takes, and the case two of them got wrong.
///
/// `--capture-mapping --analog` wrote a PNG called "--analog" and ran no capture; `--ratchet
/// --selftest` looked up a task whose id was "--selftest" and exited without selftesting. Both
/// silently - the argument was accepted, so nothing was refused, and PP306's unknown-flag check
/// could not help because both spellings ARE known flags.
/// </summary>
public class FlagArgumentTests
{
    /// <summary>The ordinary case: a word after the flag is the flag's value.</summary>
    [Fact]
    public void AWordAfterTheFlagIsItsValue()
    {
        Assert.Equal("out.png", HostCommandLine.ValueAfter(["--capture-mapping", "out.png"], "--capture-mapping"));
        Assert.Equal("PP300", HostCommandLine.ValueAfter(["--ratchet", "PP300"], "--ratchet"));
    }

    /// <summary>
    /// THE DEFECT: a flag after the flag is not its value.
    ///
    /// One case per flag that had it, spelled the way it was actually typed, so the regression is
    /// named rather than described.
    /// </summary>
    [Theory]
    [InlineData(new[] { "--capture-mapping", "--analog" }, "--capture-mapping")]
    [InlineData(new[] { "--ratchet", "--selftest" }, "--ratchet")]
    [InlineData(new[] { "--record", "--selftest" }, "--record")]
    public void AFlagAfterTheFlagIsNotItsValue(string[] args, string flag)
    {
        Assert.Null(HostCommandLine.ValueAfter(args, flag));
    }

    /// <summary>Nothing after the flag at all is the same answer.</summary>
    [Fact]
    public void NothingAfterTheFlagIsNoValue()
    {
        Assert.Null(HostCommandLine.ValueAfter(["--ratchet"], "--ratchet"));
    }

    /// <summary>And a flag that is not there has no value either.</summary>
    [Fact]
    public void AnAbsentFlagHasNoValue()
    {
        Assert.Null(HostCommandLine.ValueAfter(["--selftest"], "--ratchet"));
        Assert.False(HostCommandLine.Has(["--selftest"], "--ratchet"));
        Assert.True(HostCommandLine.Has(["--selftest"], "--selftest"));
    }

    /// <summary>
    /// TWO DASHES AND NOT ONE, which is the deliberate half of this.
    ///
    /// PP306 makes the same argument about Unrecognised: a bare word is what these flags
    /// legitimately take, and this port has never spelled anything with a single dash - so refusing
    /// one would turn a relative path into an error for no case that exists. A value that has to
    /// start with a single dash still reaches the flag.
    /// </summary>
    [Fact]
    public void ASingleDashIsStillAValue()
    {
        Assert.Equal("-", HostCommandLine.ValueAfter(["--capture-mapping", "-"], "--capture-mapping"));
        Assert.Equal("-weird.png", HostCommandLine.ValueAfter(["--capture-mapping", "-weird.png"], "--capture-mapping"));
    }

    /// <summary>Spelled either way, like every other match in the dispatch.</summary>
    [Fact]
    public void TheFlagIsMatchedWithoutRegardToCase()
    {
        Assert.Equal("out.png", HostCommandLine.ValueAfter(["--CAPTURE-MAPPING", "out.png"], "--capture-mapping"));
        Assert.True(HostCommandLine.Has(["--Ratchet"], "--ratchet"));
    }

    /// <summary>
    /// The value is read from where the flag actually is, not from the front of the line.
    ///
    /// `--map-controller --ratchet PP300` has the id two places along, and a reader that looked at
    /// args[1] would take "--ratchet" itself.
    /// </summary>
    [Fact]
    public void TheValueIsReadFromWhereTheFlagIs()
    {
        Assert.Equal(
            "PP300",
            HostCommandLine.ValueAfter(["--map-controller", "--ratchet", "PP300"], "--ratchet"));
    }

    /// <summary>
    /// Every flag documented as taking an optional argument reads it through the one rule.
    ///
    /// The list says which those are - the argument column is "[path]" or "[id]" - so a flag that
    /// grows an argument and parses it by hand is a fourth copy of the bug this fixed, and this is
    /// what notices. It asserts the rule holds for each, which is the part a hand-rolled reader
    /// would fail.
    /// </summary>
    [Fact]
    public void EveryFlagThatTakesAnArgumentAnswersTheSameWay()
    {
        IReadOnlyList<HostFlag> optional =
            [.. HostCommandLine.Flags.Where(f => f.Argument.StartsWith('['))];

        Assert.NotEmpty(optional);

        foreach (HostFlag flag in optional)
        {
            Assert.Equal("value", HostCommandLine.ValueAfter([flag.Name, "value"], flag.Name));
            Assert.Null(HostCommandLine.ValueAfter([flag.Name, "--selftest"], flag.Name));
            Assert.Null(HostCommandLine.ValueAfter([flag.Name], flag.Name));
        }
    }
}
