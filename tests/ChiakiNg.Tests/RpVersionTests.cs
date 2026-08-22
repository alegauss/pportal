using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP293: the version negotiation, exhaustively - there are only six targets.
/// </summary>
public class RpVersionTests
{
    /// <summary>Every target, and the string it speaks.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_8, "8.0")]
    [InlineData(ChiakiTarget.Ps4_9, "9.0")]
    [InlineData(ChiakiTarget.Ps4_10, "10.0")]
    [InlineData(ChiakiTarget.Ps5_1, "1.0")]
    [InlineData(ChiakiTarget.Ps4Unknown, null)]
    [InlineData(ChiakiTarget.Ps5Unknown, null)]
    public void EveryTargetHasItsVersion(ChiakiTarget target, string? expected)
        => Assert.Equal(expected, RpVersion.StringFor(target));

    /// <summary>And every string round-trips back to the target that speaks it.</summary>
    [Theory]
    [InlineData(ChiakiTarget.Ps4_8)]
    [InlineData(ChiakiTarget.Ps4_9)]
    [InlineData(ChiakiTarget.Ps4_10)]
    [InlineData(ChiakiTarget.Ps5_1)]
    public void EveryKnownTargetRoundTrips(ChiakiTarget target)
    {
        string? version = RpVersion.StringFor(target);
        Assert.NotNull(version);
        Assert.Equal(target, RpVersion.Parse(version, RpVersion.IsPs5(target)));
    }

    /// <summary>
    /// The family is not in the string, which is the trap in this table.
    ///
    /// "1.0" is a PS5 and an unknown PS4; "10.0" is a PS4 and an unknown PS5. A port that parsed
    /// the string without being told which console it came from would answer confidently and be
    /// wrong on both.
    /// </summary>
    [Fact]
    public void TheSameStringMeansDifferentThingsPerFamily()
    {
        Assert.Equal(ChiakiTarget.Ps5_1, RpVersion.Parse("1.0", isPs5: true));
        Assert.Equal(ChiakiTarget.Ps4Unknown, RpVersion.Parse("1.0", isPs5: false));

        Assert.Equal(ChiakiTarget.Ps4_10, RpVersion.Parse("10.0", isPs5: false));
        Assert.Equal(ChiakiTarget.Ps5Unknown, RpVersion.Parse("10.0", isPs5: true));
    }

    /// <summary>An unrecognised version is the family's Unknown, not a failure.</summary>
    [Theory]
    [InlineData("11.0", false, ChiakiTarget.Ps4Unknown)]
    [InlineData("2.0", true, ChiakiTarget.Ps5Unknown)]
    [InlineData("", false, ChiakiTarget.Ps4Unknown)]
    [InlineData(null, true, ChiakiTarget.Ps5Unknown)]
    [InlineData("8.00", false, ChiakiTarget.Ps4Unknown)]
    public void AnUnknownVersionIsTheFamilysUnknown(string? version, bool isPs5, ChiakiTarget expected)
        => Assert.Equal(expected, RpVersion.Parse(version, isPs5));

    /// <summary>
    /// A PS5 is decided by comparison, so a target this client has never heard of is still a PS5.
    ///
    /// The numbering leaves room above 1000000 deliberately. A port that listed the known PS5
    /// values instead would call the next console a PS4 and negotiate the wrong everything.
    /// </summary>
    [Fact]
    public void AnUnheardOfTargetAboveTheBoundaryIsStillAPs5()
    {
        Assert.True(RpVersion.IsPs5((ChiakiTarget)1000200));
        Assert.True(RpVersion.IsPs5(ChiakiTarget.Ps5Unknown));

        Assert.False(RpVersion.IsPs5(ChiakiTarget.Ps4_10));
        Assert.False(RpVersion.IsPs5((ChiakiTarget)999999));
    }

    /// <summary>Unknown is the two Unknowns and nothing else - a real target is not unknown.</summary>
    [Fact]
    public void OnlyTheTwoUnknownsAreUnknown()
    {
        Assert.True(RpVersion.IsUnknown(ChiakiTarget.Ps4Unknown));
        Assert.True(RpVersion.IsUnknown(ChiakiTarget.Ps5Unknown));

        Assert.False(RpVersion.IsUnknown(ChiakiTarget.Ps4_8));
        Assert.False(RpVersion.IsUnknown(ChiakiTarget.Ps5_1));
    }

    /// <summary>THE DRIFT CHECK. The C still holds these four strings and decides PS5 by comparison.</summary>
    [Fact]
    public void TheCStillSaysThis()
    {
        string? session = SanitizerSource.LocateRelative(SessionCoreSource.RelativePath);
        Assert.True(session is not null, "no lib\\src\\session.c - this file is describing nothing");

        Assert.True(
            SessionCoreSource.TheVersionStringsAreStill(File.ReadAllText(session), "8.0", "9.0", "10.0", "1.0"),
            "session.c no longer returns the four version strings this port sends");

        string? common = SanitizerSource.LocateRelative(@"lib\include\chiaki\common.h");
        Assert.True(common is not null, "no common.h");
        Assert.True(
            SessionCoreSource.Ps5IsStillDecidedByComparison(File.ReadAllText(common)),
            "chiaki_target_is_ps5 no longer compares, so an unheard-of target may not read as a PS5");
    }
}
