using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP335, continuing PP293: the login-PIN loop, and the flag that describes the next prompt rather
/// than this one.
///
/// PP297's capture is of a console that asked for no PIN, so none of this is in the recording. It
/// is asserted against session.c, which is why the source checks are as much of this file as the
/// behavioural ones.
/// </summary>
public class LoginPinLoopTests
{
    /// <summary>A console asking for a PIN prompts, and the first prompt does not accuse anyone.</summary>
    [Fact]
    public void TheFirstPromptDoesNotSayTheLastOneWasWrong()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(CtrlLoginPinRequested: true), prompted: false, PinWait.ForThePerson);

        Assert.Equal(PinStep.Prompt, turn.Step);
        Assert.False(turn.PinIncorrect);
    }

    /// <summary>
    /// THE SECOND PROMPT IS THE REFUSAL, and nothing ever told the loop so.
    ///
    /// pin_incorrect is assigned true straight after the first prompt is sent - before the wait,
    /// unconditionally - so it describes the NEXT prompt. No ctrl signal reports a rejected PIN;
    /// the console asking again IS the rejection. A port that waited for such a signal would show
    /// "PIN incorrect" never.
    /// </summary>
    [Fact]
    public void TheSecondPromptSaysTheLastOneWasWrong()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(CtrlLoginPinRequested: true), prompted: true, PinWait.ForTheConsole);

        Assert.Equal(PinStep.Prompt, turn.Step);
        Assert.True(turn.PinIncorrect);
    }

    /// <summary>Which is a function of the count and of nothing else.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void WhatAPromptSaysDependsOnlyOnHowManyHaveGoneOut(int soFar, bool wrong)
    {
        Assert.Equal(wrong, LoginPinLoop.SaysTheLastOneWasWrong(soFar));
    }

    /// <summary>A PIN typed is forwarded to ctrl.</summary>
    [Fact]
    public void APinTypedIsForwarded()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(LoginPinEntered: true), prompted: false, PinWait.ForThePerson);

        Assert.Equal(PinStep.Forward, turn.Step);
    }

    /// <summary>And a session id ends the loop.</summary>
    [Fact]
    public void ASessionIdEndsTheLoop()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(CtrlSessionIdReceived: true), prompted: true, PinWait.ForTheConsole);

        Assert.Equal(PinStep.Done, turn.Step);
    }

    /// <summary>
    /// Stop wins over everything, including a PIN that was just typed.
    ///
    /// The order is session.c's at all five wait sites, and it is not cosmetic: a session asked to
    /// stop while a PIN sat in the buffer would otherwise forward it and carry on connecting.
    /// </summary>
    [Fact]
    public void StopWinsOverAPinThatWasJustTyped()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(ShouldStop: true, LoginPinEntered: true),
            prompted: true, PinWait.ForThePerson);

        Assert.Equal(PinStep.Stopped, turn.Step);
    }

    /// <summary>
    /// And ctrl failing during PIN entry ends the session rather than forwarding to a dead ctrl.
    ///
    /// session.c checks ctrl_failed separately after the PIN wait, with its own message. Without
    /// that check the PIN goes to a ctrl that is gone.
    /// </summary>
    [Fact]
    public void CtrlFailingWhileThePersonTypesEndsTheSession()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(CtrlFailed: true, LoginPinEntered: true),
            prompted: false, PinWait.ForThePerson);

        Assert.Equal(PinStep.CtrlFailed, turn.Step);
    }

    /// <summary>Stop is read before ctrl failure, as it is everywhere else.</summary>
    [Fact]
    public void StopIsReadBeforeCtrlFailure()
    {
        PinTurn turn = LoginPinLoop.Next(
            new SessionState(ShouldStop: true, CtrlFailed: true), prompted: false, PinWait.ForThePerson);

        Assert.Equal(PinStep.Stopped, turn.Step);
    }

    /// <summary>
    /// ONE WAIT HAS NO TIMEOUT, and it is the one a person is inside.
    ///
    /// Every other wait in the connect sequence is bounded. session.c passes UINT64_MAX for PIN
    /// entry, because capping it would end the session of anyone who walked to the console.
    /// </summary>
    [Fact]
    public void OnlyTheWaitForThePersonIsUnbounded()
    {
        Assert.False(LoginPinLoop.IsBounded(PinWait.ForThePerson));
        Assert.True(LoginPinLoop.IsBounded(PinWait.ForTheConsole));
    }

    /// <summary>
    /// A whole wrong-then-right exchange, in the order the thread walks it.
    ///
    /// Written out because the flag's meaning only shows across iterations: prompt, forward, the
    /// console asks again, prompt AND SAY IT WAS WRONG, forward, session id.
    /// </summary>
    [Fact]
    public void AWrongPinThenARightOneWalksTheWholeLoop()
    {
        PinTurn first = LoginPinLoop.Next(
            new SessionState(CtrlLoginPinRequested: true), prompted: false, PinWait.ForThePerson);
        Assert.Equal(PinStep.Prompt, first.Step);
        Assert.False(first.PinIncorrect);

        Assert.Equal(PinStep.Forward, LoginPinLoop.Next(
            new SessionState(LoginPinEntered: true), prompted: false, PinWait.ForThePerson).Step);

        PinTurn again = LoginPinLoop.Next(
            new SessionState(CtrlLoginPinRequested: true), prompted: true, PinWait.ForTheConsole);
        Assert.Equal(PinStep.Prompt, again.Step);
        Assert.True(again.PinIncorrect);

        Assert.Equal(PinStep.Forward, LoginPinLoop.Next(
            new SessionState(LoginPinEntered: true), prompted: true, PinWait.ForThePerson).Step);

        Assert.Equal(PinStep.Done, LoginPinLoop.Next(
            new SessionState(CtrlSessionIdReceived: true), prompted: true, PinWait.ForTheConsole).Step);
    }

    /// <summary>And session.c still has the loop this reproduces.</summary>
    [Fact]
    public void SessionStillDeclaresTheLoop()
    {
        string? path = LoginPinSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(
            LoginPinSource.TheFlagIsStillSetBeforeTheWait(core),
            "pin_incorrect is no longer set after the prompt and before the wait");
        Assert.True(
            LoginPinSource.ThePromptWaitIsStillUnbounded(core),
            "the wait for PIN entry has grown a timeout");
        Assert.True(
            LoginPinSource.CtrlFailureIsStillCheckedInsideTheLoop(core),
            "ctrl failure is no longer checked inside the PIN loop");
    }
}
