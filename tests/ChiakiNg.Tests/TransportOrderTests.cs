using ChiakiNg.Protocol;
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

        // PP295 HAS SHIPPED, which is the release this rule was for arriving. It is no longer an
        // open line to declare anything, and the four behind it moved: PP696 and PP697 landed on
        // the order this dep would have jammed. A line that came back would be read below.
        string? line = TransportOrder.LineFor(roadmap, "PP295");
        if (line is null)
        {
            // And it is where a shipped line goes, rather than simply absent - which is what tells
            // "it finished" from "somebody deleted it".
            Assert.True(
                TransportOrder.LocateLedger() is { } ledger
                    && File.ReadAllText(ledger).Contains("**PP295**", StringComparison.Ordinal),
                "PP295 is in neither the roadmap nor the ledger, so the order this rule protects "
                    + "rests on a line nothing records");

            return;
        }

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
    ///
    /// PP666: and the premise is now read as MEANING rather than as one sentence. Rewriting the
    /// criterion to name PP295 outright - a stronger statement of the very thing this holds - turned
    /// this red, because the check was matching the wording it had been written beside. The two
    /// tests below are what it reads now.
    /// </summary>
    [Fact]
    public void TheFourthCriterionIsStillTheEndState()
    {
        if (Roadmap() is not { } roadmap)
            return;

        // PP295 HAS SHIPPED, and that turns this premise over rather than ending it. While PP295
        // was open, releasing it from a dep on PP27 rested on PP27's deletion waiting on PP295 - so
        // a criterion that stopped saying so made the release unjustified. Now the order has run,
        // and PP690's rule applies from the other side: a criterion naming a shipped id tells a
        // planner work is left where none is. Both readings are held, on the side that is true.
        bool shipped = TransportOrder.LocateLedger() is { } ledger
            && File.ReadAllText(ledger).Contains("**PP295**", StringComparison.Ordinal);

        if (!shipped)
        {
            Assert.True(
                TransportOrder.TheEndStateIsStillTheEndState(roadmap),
                "PP27's deletion criterion no longer reads as the end state waiting on PP295, so "
                    + "PP636's release of PP295 was made against a line that has changed");

            return;
        }

        Assert.True(
            TransportOrder.TheEndStateIsStillAnEndState(roadmap),
            "PP27's deletion criterion is no longer an end state, or still waits on PP295 after "
                + "PP295 shipped - which understates what is left to whoever reads it next");
    }

    /// <summary>
    /// PP666: a criterion that keeps the words and drops the wait is NOT the premise.
    ///
    /// This is the half the old literal match could not have: "end state" said about nothing in
    /// particular leaves PP295 waiting on a criterion that no longer waits on PP295, which is the
    /// exact condition PP636's release rules out.
    /// </summary>
    [Fact]
    public void TheWordsWithoutTheWaitAreNotThePremise()
    {
        string kept = $"""
            ## Done when — PP27

            - **{ChiakiNg.Session.TransportOrder.EndStateCriterion}** An end state, not a progress
              bar: porting into app removes no C.
            """;

        Assert.False(TransportOrder.TheEndStateIsStillTheEndState(kept));

        string whole = $"""
            ## Done when — PP27

            - **{ChiakiNg.Session.TransportOrder.EndStateCriterion}** An end state, not a progress
              bar: takion.c cannot leave until PP295 has landed.
            """;

        Assert.True(TransportOrder.TheEndStateIsStillTheEndState(whole));
    }

    /// <summary>And a different spelling of the same two words still reads as the end state.</summary>
    [Fact]
    public void TheSpellingOfTheReasonIsNotTheAssertion()
    {
        string other = $"""
            ## Done when — PP27

            - **{ChiakiNg.Session.TransportOrder.EndStateCriterion}** This is the end state and not a
              progress bar, and it waits on PP295.
            """;

        Assert.True(TransportOrder.TheEndStateIsStillTheEndState(other));
    }

    /// <summary>
    /// PP636: and the callers are really there, which is what makes the fourth unreachable now.
    ///
    /// Read from lib/ rather than trusted: the list is the argument, so a file that stopped calling
    /// takion should shorten it and one that started should lengthen it. streamconnection.c being
    /// among them is the whole point - PP295's own subject is what the criterion waits on.
    /// </summary>
    [Fact]
    public void TheCallersAreStillThere()
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
    /// <summary>
    /// PP782: THE TWO THAT CALL TAKION AND ARE NOT BLOCKERS, each for its own reason.
    ///
    /// The list above was read out of the sources and PP780 asked the linker, which is the
    /// difference between a call and a reference. audioreceiver.c calls three functions that are
    /// static inline in the header, so the link asks nobody for them; audiosender.c calls a real
    /// export from an object nothing pulls. Both still CALL takion, which is why an absence from
    /// the blocker list needs a row saying so rather than being a shorter list.
    /// </summary>
    [Fact]
    public void TheTwoThatCallItAreNotWhatTheDeletionWaitsOn()
    {
        Assert.Equal(2, TransportOrder.CallButNotLinked.Count);

        foreach ((string file, string why) in TransportOrder.CallButNotLinked)
        {
            Assert.False(string.IsNullOrWhiteSpace(why));
            Assert.DoesNotContain(file, TransportOrder.StillCallTakion);

            if (SanitizerSource.LocateRelative(file) is not { } path)
                continue;

            // It really does call takion, which is what makes its absence a claim.
            Assert.Contains("chiaki_takion_", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// AND EVERY SYMBOL AUDIORECEIVER NAMES IS ONE THE HEADER DEFINES INLINE.
    ///
    /// The permanent half of PP782. If it ever calls an exported takion function, it becomes a
    /// blocker and this is what says so - the other row's reason is about this tree and would need
    /// a different check.
    /// </summary>
    [Fact]
    public void EverySymbolTheReceiverNamesIsInline()
    {
        if (SanitizerSource.LocateRelative(TransportOrder.CallButNotLinked[0].File) is not { } path)
            return;

        IReadOnlyList<string> called = TakionConsumers.CallsIn(File.ReadAllText(path));

        Assert.NotEmpty(called);
        Assert.All(called, one => Assert.Contains(one, TakionConsumers.InlineInTheHeader));
    }
}
