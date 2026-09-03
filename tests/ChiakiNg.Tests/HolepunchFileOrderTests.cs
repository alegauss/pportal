using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP655, PP663 and PP664, under PP33: the second flip's order, the hazard that made its first step
/// compulsory, the flip itself, and the prose turn that closes it at three of three.
///
/// PP623 wrote the first order and PP634 found its third step wrong - written before the first two
/// landed, and wrong in the way only their landing made visible. So an order is worth asserting as
/// well as writing: what a plan claims about itself is checkable even when the work is not done.
/// </summary>
public class HolepunchFileOrderTests
{
    /// <summary>
    /// The order is landable by the same rule the first one is measured against.
    ///
    /// Exactly one step edits the C, and it is neither the first nor the last: the first carries the
    /// preparation that makes the flip one commit, the last the clean-up only possible after it. The
    /// rule is reused rather than restated, which is what PP634 said the plan was for.
    /// </summary>
    [Fact]
    public void TheOrderIsLandable()
        => Assert.True(HolepunchDeletionOrder.IsLandable(HolepunchFileOrder.Stages));

    /// <summary>And it is three steps, with the flip in the middle, and two have landed.</summary>
    [Fact]
    public void ThereAreThreeStepsAndTheFlipIsTheSecond()
    {
        Assert.Equal(3, HolepunchFileOrder.Stages.Count);
        Assert.Equal(HolepunchFileOrder.Flip, HolepunchFileOrder.Stages[1]);
        Assert.Equal(HolepunchFileOrder.Stages.Count, HolepunchFileOrder.Landed);
    }

    /// <summary>
    /// PP663: the option exists and the build declares it, which is the flip having landed.
    ///
    /// The option is the whole of what the flip is. A tree where it is gone is one where somebody
    /// either finished PP33 or reverted this, and either way the order's Landed count is wrong.
    /// </summary>
    [Fact]
    public void TheOptionTheFlipIntroducedIsDeclared()
    {
        if (SanitizerSource.LocateRelative(HolepunchFileOrder.RootCMakeRelativePath) is not { } path)
            return;

        Assert.Contains(
            $"option({HolepunchFileOrder.ProposedOption}",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// PP663: and the configure line passes it explicitly, which is PP21's finding inherited.
    ///
    /// option() does not override a value already in the cache, so a default is correct for a fresh
    /// clone and inert everywhere else. Passed on the command line it overrides on every configure -
    /// without which a stale ON keeps curl, json-c and both oracles in a tree whose author had
    /// turned them off, and nothing says so.
    /// </summary>
    [Fact]
    public void TheConfigureLinePassesItRatherThanTrustingTheDefault()
    {
        if (SanitizerSource.LocateRelative(HolepunchFileOrder.ConfigureScriptRelativePath) is not { } path)
            return;

        Assert.Contains(
            $"-D{HolepunchFileOrder.ProposedOption}=",
            File.ReadAllText(path),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The hazard is reachable, which is what makes the first step compulsory rather than tidy.
    ///
    /// All twelve of the shim's holepunch declarations are in the header NativeSeam reads. So a flip
    /// that gated the bodies and left the header would leave that census green with nine exports
    /// gone - and the failure arrives as EntryPointNotFoundException on the first call, inside a
    /// live session. Asserted rather than described, because a hazard nobody can reach is a
    /// paragraph and one that can is a reason.
    /// </summary>
    [Fact]
    public void TheWrappersAreDeclaredInTheHeaderTheCensusReads()
    {
        if (SanitizerSource.LocateRelative(HolepunchFileOrder.ShimHeaderRelativePath) is not { } path)
            return;

        string header = File.ReadAllText(path);

        // Counted, because the wrapper's name is not the C symbol's - the device id's C is
        // chiaki_holepunch_generate_client_device_uid and its wrapper is chiaki_shim_generate_
        // client_device_uid. What matters is that the header declares at least as many of them as
        // the shim defines, so gating one file and not the other is a thing somebody could do.
        int declared = header.Split("chiaki_shim_holepunch", StringSplitOptions.None).Length - 1
            + header.Split("chiaki_shim_generate_client_device_uid", StringSplitOptions.None).Length - 1;

        Assert.True(
            declared >= HolepunchShimSurface.UndefinedReferences.Count,
            $"the header declares {declared} of the shim's holepunch entry points and the shim "
                + $"defines {HolepunchShimSurface.UndefinedReferences.Count}, so the contract "
                + "NativeSeam reads is already narrower than the DLL and the hazard has changed");

        // And that header really is one the census reads, which is the whole of why it matters.
        Assert.Contains(
            HolepunchFileOrder.ShimHeaderRelativePath,
            NativeSeam.HeaderRelativePaths);
    }

    /// <summary>
    /// The flip names the four things it has to carry, and none of them is a literal here.
    ///
    /// Deleting one of those pieces elsewhere should be a build error rather than a plan that
    /// quietly stopped describing the work, which is the first order's own reason for the same list.
    /// </summary>
    [Fact]
    public void TheFlipNamesWhatItCarries()
    {
        Assert.Contains(HolepunchShimSurface.SourceEntry, HolepunchFileOrder.FlipCarries);
        Assert.Contains(HolepunchFileOrder.ProposedOption, HolepunchFileOrder.FlipCarries);
        Assert.Equal(
            HolepunchFileOrder.FlipCarries.Count,
            HolepunchFileOrder.FlipCarries.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// And the flip's own detail names the header, because that is the half a reader would drop.
    ///
    /// The obvious flip is "take the file out of the build". It is the one that leaves the census
    /// green and the runtime broken, so the step's own words have to carry the header rather than
    /// leaving it to the paragraph above them.
    /// </summary>
    [Fact]
    public void TheFlipsOwnWordsCarryTheHeader()
    {
        Assert.Contains("HEADER", HolepunchFileOrder.Flip.Detail, StringComparison.Ordinal);
        Assert.Contains("NativeSeam", HolepunchFileOrder.HeaderHazard, StringComparison.Ordinal);
    }
}
