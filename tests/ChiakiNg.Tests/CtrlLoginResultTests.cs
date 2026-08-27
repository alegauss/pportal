using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP408, under PP294: what the console answers a PIN with.
///
/// PP335 has the session side of the loop and PP345 the handover into ctrl. This is the handler
/// between them - the message that says the PIN was right, or was not, or says nothing anyone named.
/// </summary>
public class CtrlLoginResultTests
{
    /// <summary>The right PIN stops ctrl considering one outstanding.</summary>
    [Fact]
    public void SuccessClearsTheOutstandingPin()
    {
        LoginOutcome outcome = CtrlLoginResult.Receive([(byte)CtrlLoginState.Success], pinOutstanding: true);

        Assert.Equal(LoginOutcome.Accepted, outcome);
        Assert.False(CtrlLoginResult.StillOutstanding(outcome, wasOutstanding: true));
        Assert.False(CtrlLoginResult.AsksTheSessionToPrompt(outcome));
    }

    /// <summary>The wrong one asks the session to prompt again, and leaves the flag as it was.</summary>
    [Fact]
    public void AnIncorrectPinPromptsAgain()
    {
        LoginOutcome outcome =
            CtrlLoginResult.Receive([(byte)CtrlLoginState.PinIncorrect], pinOutstanding: true);

        Assert.Equal(LoginOutcome.PromptAgain, outcome);
        Assert.True(CtrlLoginResult.AsksTheSessionToPrompt(outcome));

        // Still outstanding, which is what lets the console refuse twice and be prompted twice.
        Assert.True(CtrlLoginResult.StillOutstanding(outcome, wasOutstanding: true));
    }

    /// <summary>
    /// THE PROPERTY WORTH HAVING A NAME FOR. A refusal nobody asked for raises no prompt.
    ///
    /// This is what stands between a stray control message and a PIN dialog over somebody's stream.
    /// </summary>
    [Fact]
    public void AnUnsolicitedRefusalRaisesNoPrompt()
    {
        LoginOutcome outcome =
            CtrlLoginResult.Receive([(byte)CtrlLoginState.PinIncorrect], pinOutstanding: false);

        Assert.Equal(LoginOutcome.Unsolicited, outcome);
        Assert.False(CtrlLoginResult.AsksTheSessionToPrompt(outcome));
        Assert.False(CtrlLoginResult.StillOutstanding(outcome, wasOutstanding: false));
    }

    /// <summary>A state neither value names is logged and does nothing.</summary>
    [Theory]
    [InlineData((byte)0x02)]
    [InlineData((byte)0x7f)]
    [InlineData((byte)0xff)]
    public void AnUnnamedStateDoesNothing(byte state)
    {
        Assert.Equal(LoginOutcome.Unknown, CtrlLoginResult.Receive([state], pinOutstanding: true));
        Assert.False(CtrlLoginResult.AsksTheSessionToPrompt(
            CtrlLoginResult.Receive([state], pinOutstanding: true)));
    }

    /// <summary>An empty payload has no state to read, so nothing happens.</summary>
    [Fact]
    public void AnEmptyPayloadIsIgnored()
    {
        Assert.Equal(LoginOutcome.Ignored, CtrlLoginResult.Receive([], pinOutstanding: true));
        Assert.True(CtrlLoginResult.IsRefused(0));
    }

    /// <summary>
    /// AND AN OVERSIZE ONE IS WARNED ABOUT AND USED. The guard reports rather than refuses.
    ///
    /// Two bytes draw the warning and the first is still read, which is a different kind of guard
    /// from the ones that return - and the reason a port that refused here would be stricter than
    /// the console it is talking to.
    /// </summary>
    [Fact]
    public void AnOversizePayloadIsWarnedAboutAndStillRead()
    {
        Assert.True(CtrlLoginResult.IsWarnedAbout(2));
        Assert.False(CtrlLoginResult.IsRefused(2));

        Assert.Equal(
            LoginOutcome.Accepted,
            CtrlLoginResult.Receive([(byte)CtrlLoginState.Success, 0xff], pinOutstanding: true));

        // And exactly one byte draws no warning at all.
        Assert.False(CtrlLoginResult.IsWarnedAbout(1));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheHandlersRulesAreStillTheQtCores()
    {
        string? path = CtrlLoginResultSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.NotNull(CtrlLoginResultSource.Body(core));
        Assert.True(CtrlLoginResultSource.TheStatesAreStillThese(core), "0x0 and 0x1");
        Assert.True(
            CtrlLoginResultSource.TheGuardStillReportsRatherThanRefuses(core),
            "the size guard no longer warns before it refuses");
        Assert.True(
            CtrlLoginResultSource.AnUnsolicitedRefusalStillDoesNothing(core),
            "a refusal nobody asked for can raise the session's flag again");
        Assert.True(
            CtrlLoginResultSource.SuccessStillClearsOnlyCtrlsFlag(core),
            "success no longer clears ctrl's flag alone");
    }

    /// <summary>
    /// And the far side: the session is still the one that clears its own flag.
    ///
    /// This is the check behind the class note. Success clearing ctrl's flag and not the session's
    /// reads as an omission, and it is not one - so the claim is held against session.c rather than
    /// left as prose.
    /// </summary>
    [Fact]
    public void TheSessionStillConsumesItsOwnFlag()
    {
        string? path = ChiakiNg.Session.SanitizerSource.LocateRelative(@"lib\src\session.c");
        if (path is null)
            return;

        Assert.True(
            CtrlLoginResultSource.TheSessionStillConsumesItsOwnFlag(File.ReadAllText(path)),
            "nothing in session.c consumes ctrl_login_pin_requested, so the flag would go stale");
    }

    /// <summary>PP272: and every reader answers no to an empty file.</summary>
    [Fact]
    public void EveryReaderAnswersNoToAnEmptyFile()
    {
        Assert.Null(CtrlLoginResultSource.Body(""));
        Assert.False(CtrlLoginResultSource.TheStatesAreStillThese(""));
        Assert.False(CtrlLoginResultSource.TheGuardStillReportsRatherThanRefuses(""));
        Assert.False(CtrlLoginResultSource.AnUnsolicitedRefusalStillDoesNothing(""));
        Assert.False(CtrlLoginResultSource.SuccessStillClearsOnlyCtrlsFlag(""));
        Assert.False(CtrlLoginResultSource.TheSessionStillConsumesItsOwnFlag(""));
    }
}
