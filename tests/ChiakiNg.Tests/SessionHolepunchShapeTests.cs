using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP630: the mechanism PP623's first step needs, and the trap it is shaped to avoid.
///
/// PP621 counted ten models reading session.c; PP623 settled that the flip commit edits the C and no
/// test file. That only works if every one of those models already accepts both shapes when it
/// arrives - and a guard added carelessly turns a check into one that reports green because it
/// declined to look, which is what PP56 and PP226 were both filed for.
///
/// So what is asserted is the PAIR: exactly one side answers on any tree, and the silent side has
/// assertions of its own rather than an absence of them.
/// </summary>
public class SessionHolepunchShapeTests
{
    /// <summary>
    /// PP630: the shape is the handle, and nothing else.
    ///
    /// Keyed on <see cref="HolepunchDirection.Handle"/> because every one of the nine is reached
    /// through it - the assignment, the guards, the two fini sites. A file that had lost one call
    /// and kept the field is not a shape this models, and PP623 makes the flip one commit precisely
    /// so no such tree exists.
    /// </summary>
    [Fact]
    public void TheShapeIsWhetherTheHandleIsNamed()
    {
        Assert.Equal(
            SessionShape.Asking,
            SessionHolepunchShape.Of($"if({HolepunchDirection.Handle})\n\treturn;"));

        Assert.Equal(
            SessionShape.Silent,
            SessionHolepunchShape.Of("if(session->rudp)\n\treturn;"));

        // A file that talks about holepunch without naming the handle is silent, which is what
        // session.c looks like after the flip: comments outlive the calls.
        Assert.Equal(
            SessionShape.Silent,
            SessionHolepunchShape.Of("// PP590: recorded here so ctrl.c does not ask holepunch.c"));
    }

    /// <summary>
    /// PP630: EXACTLY ONE SIDE ANSWERS, which is what makes the guard a guard.
    ///
    /// The failure this is against is silent in both directions. Two answers is a shape nothing
    /// modelled; none at all is every check on both sides returning early while session.c sits
    /// there being perfectly readable - a suite that passes because it stopped asking.
    /// </summary>
    [Fact]
    public void ExactlyOneSideAnswersOnThisTree()
    {
        Assert.True(
            SessionHolepunchShape.ExactlyOneShapeAnswers(),
            "both readings answered, or neither did, on a tree that has session.c");

        if (SessionHolepunchShape.Locate() is null)
            return;

        // And on a checkout, one of them really is a source rather than both being null.
        Assert.True(
            SessionHolepunchShape.AskingSource() is not null
                || SessionHolepunchShape.SilentSource() is not null);
    }

    /// <summary>
    /// PP632: this tree is the SILENT one now, and this is the assertion that turned over to say so.
    ///
    /// It was a tripwire on purpose. PP630 asserted that session.c still asked, so the commit that
    /// stopped it had to come here and say so rather than sliding past - which is why PP623's "the
    /// flip edits no test file" was approximate by exactly one, and this is the one.
    ///
    /// What it asserts now is the deletion itself: the handle is gone from session.c and so is the
    /// export that carries no `chiaki_` prefix, which PP564 found because a sweep keyed on that
    /// prefix walks straight past it.
    /// </summary>
    [Fact]
    public void ThisTreeHasStoppedAsking()
    {
        if (SessionHolepunchShape.Locate() is null)
            return;

        string? silent = SessionHolepunchShape.SilentSource();

        Assert.True(silent is not null, "session.c names the holepunch handle again");
        Assert.Empty(SessionHolepunchShape.StillPresentIn(silent!));
    }

    /// <summary>
    /// PP630: and the silent side is a CHECK, not an absence.
    ///
    /// This is the half that makes the pair honest. When the flip lands, `AskingSource` starts
    /// answering null and every converted model stops asserting - so something has to assert that
    /// the reason is the deletion having happened rather than a reader that broke. That is this.
    /// </summary>
    [Fact]
    public void TheSilentSideAssertsTheDeletionRatherThanNothing()
    {
        // The rule, exercised on both shapes so it is a rule and not a description of one tree.
        Assert.Equal(
            SessionHolepunchShape.GoneWhenSilent,
            SessionHolepunchShape.StillPresentIn(
                $"{HolepunchDirection.Handle} and {HolepunchConsumers.UnprefixedExport}("));

        Assert.Empty(SessionHolepunchShape.StillPresentIn("nothing of the sort"));

        // PP564: the export with no chiaki_ prefix is in the list, because a sweep keyed on that
        // prefix walks straight past it - and a flip that missed it leaves session.c calling into
        // the file it was deleting.
        Assert.Contains(HolepunchConsumers.UnprefixedExport, SessionHolepunchShape.GoneWhenSilent);

        if (SessionHolepunchShape.SilentSource() is { } silent)
            Assert.Empty(SessionHolepunchShape.StillPresentIn(silent));
    }

    /// <summary>
    /// PP631: every converted test still goes through the pair, and none has gone back.
    ///
    /// The failure this is against is green today and red in exactly one commit: a file that
    /// returned to locating and reading session.c itself passes now and fails in PP33's flip, which
    /// is the one commit PP623 exists to keep free of test edits. A drift check is the only thing
    /// that catches it before then.
    /// </summary>
    [Fact]
    public void EveryConvertedTestStillReadsThroughTheShape()
    {
        if (ChiakiNg.Session.SanitizerSource.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<string> back = SessionHolepunchShape.NotReadingThroughTheShape(root);

        Assert.True(
            back.Count == 0,
            $"these read session.c themselves again, so PP33's flip would turn them red: "
                + string.Join(", ", back));

        // And the list is not empty, which is what a check about nothing would look like.
        Assert.NotEmpty(SessionHolepunchShape.ConvertedTests);
    }

    /// <summary>
    /// PP630: the mechanism converts no model yet, and that is what makes the step landable.
    ///
    /// PP623's first stage is per-model and each conversion is its own commit. This one is the thing
    /// they share, shipped on its own so the ten that follow are each a small, green change.
    /// </summary>
    [Fact]
    public void TheCensusIsStillWhatItWas()
    {
        if (ChiakiNg.Session.SanitizerSource.RepositoryRoot() is not { } root)
            return;

        // Unconverted models still quote the handle, so the census still finds them. When they stop,
        // it is because they were converted - which is the count PP621 exists to make readable.
        Assert.Contains(HolepunchOracleReaders.Census(root), one => !one.IsTest);
    }
}
