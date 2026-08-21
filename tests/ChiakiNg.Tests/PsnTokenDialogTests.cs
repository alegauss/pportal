using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP15: the token dialog, and the three ways it is not the login dialog.
/// </summary>
public class PsnTokenDialogTests
{
    /// <summary>The heading turns on one property, and the log's own heading does not turn at all.</summary>
    [Fact]
    public void TheHeadingSaysWhetherSomethingExpired()
    {
        var model = new PsnTokenViewModel();
        Assert.Equal(PsnTokenViewModel.SetupTitle, model.Title);

        model.Expired = true;
        Assert.Equal(PsnTokenViewModel.ExpiredTitle, model.Title);
    }

    /// <summary>
    /// The button has no submission guard, where the login dialog's identical-looking one does.
    /// Asserted against the login model so the difference is the assertion rather than a comment.
    /// </summary>
    [Fact]
    public void TheButtonAcceptsASecondPressWhereTheLoginsDoesNot()
    {
        var token = new PsnTokenViewModel { RedirectUrl = "  " + PsnAuth.RedirectPage + "?code=A  " };
        Assert.True(token.CanSubmit);

        var login = new PsnLoginViewModel
        {
            Mode = PsnLoginMode.Redirect,
            RedirectUrl = PsnAuth.RedirectPage + "?code=A",
            Submitting = true,
        };

        // Same URL, same shape of button, and only one of them refuses while a request is running.
        Assert.False(login.CanSubmit);
        Assert.True(token.CanSubmit);
    }

    /// <summary>And blank is blank on both, which is the half they do share.</summary>
    [Fact]
    public void ABlankUrlEnablesNothing()
        => Assert.False(new PsnTokenViewModel { RedirectUrl = "   " }.CanSubmit);

    /// <summary>
    /// The ask is turned on by opening the screen and off by a setup that worked - and that flag
    /// is what PP7's browser reads to decide whether closing it clears cookies. So an abandoned
    /// setup clears the cookies behind it and a successful one leaves the user signed in.
    /// </summary>
    [Fact]
    public void TheAskIsSetByOpeningAndClearedBySucceeding()
    {
        var model = new PsnTokenViewModel();

        model.Opened();
        Assert.True(model.RemotePlayAsk);
        Assert.True(PsnBrowser.ClearsCookiesOnClose(model.RemotePlayAsk));

        model.Submit();
        model.Report("[I] PSN Remote Connection Tokens Generated.", ok: true, done: true);

        Assert.False(model.RemotePlayAsk);
        Assert.False(PsnBrowser.ClearsCookiesOnClose(model.RemotePlayAsk));
    }

    /// <summary>A setup that failed leaves the ask on, so its cookies go on the way out.</summary>
    [Fact]
    public void AFailedSetupLeavesTheAskOn()
    {
        var model = new PsnTokenViewModel();
        model.Opened();
        model.Submit();

        model.Report("[E] Invalid code from redirect url.", ok: false, done: true);

        Assert.True(model.RemotePlayAsk);
        Assert.Equal(PsnTokenLogState.Finished, model.LogState);
    }

    /// <summary>
    /// The log accumulates every message in order and ends by changing its button - which is the
    /// only signal there is that the setup stopped.
    /// </summary>
    [Fact]
    public void TheLogAccumulatesAndEndsByChangingItsButton()
    {
        var model = new PsnTokenViewModel();
        model.Submit();

        Assert.True(model.LogOpen);
        Assert.Equal("", model.LogText);
        Assert.Equal("Cancel", model.LogButtonText);

        model.Report("[I] first", ok: false, done: false);
        model.Report("[I] second", ok: false, done: false);

        Assert.Equal("[I] first\n[I] second\n", model.LogText);
        Assert.Equal("Cancel", model.LogButtonText);

        model.Report("[I] done", ok: true, done: true);

        Assert.Equal("[I] first\n[I] second\n[I] done\n", model.LogText);
        Assert.Equal("Close", model.LogButtonText);
    }

    /// <summary>
    /// Return works only once the button has changed; Escape works throughout. Two keys, two
    /// rules, and the one that is guarded is the one a user presses to make something happen.
    /// </summary>
    [Fact]
    public void ReturnWaitsForTheEndAndEscapeDoesNot()
    {
        var model = new PsnTokenViewModel();
        model.Submit();

        Assert.False(model.ReturnClosesTheLog);
        Assert.True(PsnTokenViewModel.EscapeClosesTheLog);

        model.Report("done", ok: true, done: true);
        Assert.True(model.ReturnClosesTheLog);
    }

    /// <summary>Pressing Setup a second time starts the log over rather than appending to it.</summary>
    [Fact]
    public void ASecondSubmitStartsTheLogOver()
    {
        var model = new PsnTokenViewModel();
        model.Submit();
        model.Report("[E] no", ok: false, done: true);

        model.Submit();

        Assert.Equal("", model.LogText);
        Assert.Equal(PsnTokenLogState.Running, model.LogState);
    }

    /// <summary>The copy button appears only when there is a URL to copy.</summary>
    [Fact]
    public void TheCopyButtonNeedsAUrl()
    {
        var model = new PsnTokenViewModel();
        Assert.False(model.CopyVisible);

        model.LoginUrl = PsnAuth.LoginUrl("duid");
        Assert.True(model.CopyVisible);
    }

    /// <summary>Every rule above, still stated the same way in the two dialogs.</summary>
    [Fact]
    public void TheTokenDialogsRulesAreStillTheQtClients()
    {
        string? token = PsnTokenDialogSource.Locate();
        string? login = PsnLoginSource.Locate();
        if (token is null || login is null)
            return;

        string qml = File.ReadAllText(token);

        Assert.True(PsnTokenDialogSource.TheTitleFollowsExpired(qml), "two headings");
        Assert.True(
            PsnTokenDialogSource.TheButtonHasNoSubmissionGuard(qml, File.ReadAllText(login)),
            "one button guarded, the other not");
        Assert.True(PsnTokenDialogSource.TheAskIsSetAtBothEnds(qml), "the ask, both ends");
        Assert.True(PsnTokenDialogSource.TheLogEndsByChangingItsButton(qml), "Cancel becomes Close");
        Assert.True(PsnTokenDialogSource.ReturnIsGuardedOnThatButton(qml), "Return waits for it");
        Assert.True(PsnTokenDialogSource.TheLogHasNoAutoClose(qml), "no click-away");
        Assert.True(PsnTokenDialogSource.ClosingTheLogLeavesForTheMainView(qml), "and it leaves");
    }
}
