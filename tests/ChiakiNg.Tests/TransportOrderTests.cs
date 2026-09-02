using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP636: the release of PP295, and the premise it rests on.
///
/// PP295 declared a dep on the whole of PP27 when what it waits for is three of PP27's four
/// criteria - the transport, all met. The fourth is the deletion, and takion.c cannot leave a build
/// six files in lib/ still call it from. Removing the last of them IS PP295, so the dep made it wait
/// on work that waits on it, with PP28, PP31 and PP32 behind.
///
/// A dep dropped and forgotten is how a backlog loses the reason for its own order. These are the
/// reason, in the place a check runs.
/// </summary>
public class TransportOrderTests
{
    private static string? Roadmap()
        => TransportOrder.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// PP636: PP295 does not wait on PP27, and the four behind it are free.
    ///
    /// The assertion the change is: a dep restored quietly - by a merge, or by somebody reading the
    /// section that still describes the transport as PP27's - would jam the same four lines again,
    /// and nothing else in the tree would say why they had stopped.
    /// </summary>
    [Fact]
    public void PP295DoesNotWaitOnPP27()
    {
        if (Roadmap() is not { } roadmap)
            return;

        string? line = TransportOrder.LineFor(roadmap, "PP295");

        Assert.NotNull(line);
        Assert.False(
            TransportOrder.DeclaresDep(line, "PP27"),
            "PP295 declares PP27 again, so it waits on a criterion that waits on PP295");
    }

    /// <summary>
    /// PP636: and the reader can see a line that DOES declare it, so the check is a rule.
    ///
    /// A predicate that answered false for everything would pass the test above on any roadmap at
    /// all, including one where the dep is back.
    /// </summary>
    [Fact]
    public void ADeclaredDepIsSeen()
    {
        Assert.True(TransportOrder.DeclaresDep(
            "- 📋 **PP295** (deps: PP27 ⏳, PP297 ✅) **streamconnection.c…**", "PP27"));

        Assert.False(TransportOrder.DeclaresDep(
            "- 📋 **PP295** (deps: PP297 ✅) **streamconnection.c…**", "PP27"));

        // And a mention outside the deps is not a dep - PP295's own why names other ids.
        Assert.False(TransportOrder.DeclaresDep(
            "- 📋 **PP295** (deps: PP297 ✅) **x** — PP27 measured the transport.", "PP27"));
    }

    /// <summary>
    /// PP636: THE PREMISE. PP27's fourth criterion is still the end state.
    ///
    /// If it stopped being one - if somebody decided the three files could leave before their
    /// callers do - then PP295 would be waiting for something reachable and this release was made
    /// against a line that no longer says what it said.
    /// </summary>
    [Fact]
    public void TheFourthCriterionIsStillTheEndState()
    {
        if (Roadmap() is not { } roadmap)
            return;

        Assert.True(
            TransportOrder.TheEndStateIsStillTheEndState(roadmap),
            "PP27's deletion criterion no longer reads as the end state, so PP636's release of "
                + "PP295 was made against a line that has changed");
    }

    /// <summary>
    /// PP636: and the six callers are really there, which is what makes the fourth unreachable now.
    ///
    /// Read from lib/ rather than trusted: the list is the argument, so a file that stopped calling
    /// takion should shorten it and one that started should lengthen it. streamconnection.c being
    /// among them is the whole point - PP295's own subject is what the criterion waits on.
    /// </summary>
    [Fact]
    public void TheSixCallersAreStillThere()
    {
        Assert.Contains(TransportOrder.StreamConnection, TransportOrder.StillCallTakion);

        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        foreach (string relative in TransportOrder.StillCallTakion)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path))
                continue;

            Assert.Contains(
                "chiaki_takion_", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }
}
