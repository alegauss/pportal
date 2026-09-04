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
    /// PP33: the option is GONE, which is this task finishing rather than the flip landing.
    ///
    /// PP663 introduced CHIAKI_ENABLE_HOLEPUNCH so curl, json-c, holepunch.c and both oracles could
    /// leave the ordinary build while staying reachable. They have now left the tree, so there is
    /// nothing left for the option to admit - and PP663's own note said this is where it ends: "OFF
    /// by default is the point... -DCHIAKI_ENABLE_HOLEPUNCH=ON restores every piece unchanged", a
    /// sentence that stops being true the moment there is nothing to restore.
    /// </summary>
    [Fact]
    public void TheOptionTheFlipIntroducedIsGone()
    {
        if (SanitizerSource.LocateRelative(HolepunchFileOrder.RootCMakeRelativePath) is not { } path)
            return;

        Assert.DoesNotContain(
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
    public void TheConfigureLineNoLongerPassesIt()
    {
        if (SanitizerSource.LocateRelative(HolepunchFileOrder.ConfigureScriptRelativePath) is not { } path)
            return;

        // PP33: passing an option cmake does not declare is a warning nobody reads, so the -D goes
        // in the same commit as the option. The pair is what this checks - the option's absence is
        // asserted above, and a configure line still naming it would be the half-done state that
        // leaves a stale cache deciding what the build does.
        Assert.DoesNotContain(
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
    public void TheWrappersAreGoneFromTheHeaderTheCensusReads()
    {
        if (SanitizerSource.LocateRelative(HolepunchFileOrder.ShimHeaderRelativePath) is not { } path)
            return;

        string header = File.ReadAllText(path);

        // PP33: the header and the body went TOGETHER, which is the hazard this counted. The header
        // is the contract NativeSeam reads, so a flip that gated the bodies and left the
        // declarations would have left that census green while the DLL lost nine exports - and the
        // failure would arrive as EntryPointNotFoundException on the first call, inside a live
        // session. Both are zero here, which is the only pairing that is not that hazard.
        int declared = header.Split("chiaki_shim_holepunch", StringSplitOptions.None).Length - 1
            + header.Split("chiaki_shim_generate_client_device_uid", StringSplitOptions.None).Length - 1;

        Assert.Equal(0, declared);

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
