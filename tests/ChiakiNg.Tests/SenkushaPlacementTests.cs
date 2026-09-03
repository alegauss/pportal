using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP28: senkusha's place in the session thread, which is the first of the three joins left.
///
/// PP293 took session.c's own lifetime, PP294 ctrl.c and PP295 has streamconnection.c. What none of
/// them owns is the ORDER between them, and senkusha is the step that sits in the middle of it: it
/// runs after ctrl reported a session id and before the stream connection is asked for anything, and
/// what it hands back is what everything after it is sized by.
/// </summary>
public class SenkushaPlacementTests
{
    private static string? Source()
        => SenkushaPlacement.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// The three outcomes, and the middle one is the trap.
    ///
    /// Cancelled ends the session and a plain failure does not, which is the opposite of what an
    /// error code's severity suggests. A port written as "success or bail" reproduces two of these
    /// three and ends sessions that upstream carries on with.
    /// </summary>
    [Theory]
    [InlineData(SenkushaRunOutcome.Succeeded, SenkushaConsequence.Continue)]
    [InlineData(SenkushaRunOutcome.Canceled, SenkushaConsequence.EndSession)]
    [InlineData(SenkushaRunOutcome.Failed, SenkushaConsequence.FallBack)]
    public void EachOutcomeHasItsOwnConsequence(
        SenkushaRunOutcome outcome, SenkushaConsequence consequence)
        => Assert.Equal(consequence, SenkushaPlacement.After(outcome));

    /// <summary>
    /// And an init that failed has no fallback, which is the asymmetry between the two calls.
    ///
    /// Senkusha that could not be built produced no measurement AND no error code to classify, so
    /// the session leaves through the same label a cancelled run does. A port that gave init the
    /// run's three-way treatment would carry on with fallback numbers on a path upstream ends.
    /// </summary>
    [Fact]
    public void AnInitThatFailedEndsTheSession()
    {
        Assert.Equal(SenkushaConsequence.EndSession, SenkushaPlacement.AfterInitFailed());
        Assert.NotEqual(SenkushaPlacement.AfterInitFailed(), SenkushaPlacement.After(SenkushaRunOutcome.Failed));
    }

    /// <summary>
    /// The fallback carries four fields and the fourth is a boolean nobody would guess.
    ///
    /// Two MTUs and an RTT are what a reader expects from a step that measures MTUs and an RTT. The
    /// don't-fragment bit is not: senkusha's success path never writes it, so it only ever changes
    /// on the path where senkusha did not work.
    /// </summary>
    [Fact]
    public void TheFallbackClearsTheDontFragmentBitAsWellAsSupplyingNumbers()
    {
        SenkushaFallback fallback = SenkushaPlacement.Fallback;

        Assert.Equal(1454, fallback.MtuIn);
        Assert.Equal(1454, fallback.MtuOut);
        Assert.Equal(1000, fallback.RttMicroseconds);
        Assert.False(fallback.DontFragment, "the fallback leaves the don't-fragment bit set");
    }

    /// <summary>The guard is defined in the same file, so senkusha is in every build.</summary>
    [Fact]
    public void SenkushaIsNotABuildOption()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SenkushaPlacementSource.TheGuardIsDefinedInThisFile(source),
            "ENABLE_SENKUSHA is no longer defined beside its own #ifdef, so senkusha has become "
                + "something a build can turn off and this model says it cannot be");
    }

    /// <summary>
    /// Fini still runs before the outcome is read, which is what makes the cleanup unconditional.
    ///
    /// The reordering this catches is the tidy one: read the error, then clean up on each branch.
    /// It leaks on two of the three outcomes and looks more careful than what is there.
    /// </summary>
    [Fact]
    public void TheCleanupHappensBeforeTheResultIsClassified()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SenkushaPlacementSource.FiniRunsBeforeTheOutcomeIsRead(source),
            "init, run, fini and the branch on err are no longer in that order in session.c");
    }

    /// <summary>
    /// Ctrl is asked again after senkusha, because senkusha is long and ctrl is another thread.
    ///
    /// Between the fini and the outcome, and nowhere else - a check before the run would be asking
    /// about a ctrl that had not had time to die yet.
    /// </summary>
    [Fact]
    public void CtrlIsRecheckedAcrossTheLongestStep()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SenkushaPlacementSource.CtrlIsAskedAgainAfterSenkusha(source),
            "session.c no longer re-reads ctrl_failed between senkusha's fini and its outcome, so "
                + "a ctrl that died during the longest step reaches the stream connection");
    }

    /// <summary>
    /// Cancelled is the only code named, and everything else still falls back.
    ///
    /// This is the assertion the whole model rests on. If session.c ever starts naming a second
    /// fatal code, <see cref="SenkushaPlacement.After"/> is wrong for it and says Continue-or-fall
    /// back where the C now ends the session.
    /// </summary>
    [Fact]
    public void OnlyCancelledIsFatalAndTheRestFallBack()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SenkushaPlacementSource.CanceledIsTheOnlyFatalOutcome(source),
            "session.c's senkusha outcome is no longer success, then CANCELED, then the fallback");
    }

    /// <summary>And the four fields it sets are the four this model carries.</summary>
    [Fact]
    public void TheFallbackInTheCIsTheOneModelledHere()
    {
        if (Source() is not { } source)
            return;

        Assert.True(
            SenkushaPlacementSource.TheFallbackSetsFourFields(source),
            "session.c's senkusha fallback no longer sets mtu_in, mtu_out, rtt_us and dontfrag in "
                + "that order, or the numbers moved away from the ones modelled here");
    }
}
