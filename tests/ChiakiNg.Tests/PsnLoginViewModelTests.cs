using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP7: the login's two browserless paths, which is everything the WebView2 control is not.
/// </summary>
public class PsnLoginViewModelTests
{
    /// <summary>The embedded path has no button, so there is nothing for a rule to enable.</summary>
    [Fact]
    public void TheBrowserPathHasNoButton()
    {
        var model = new PsnLoginViewModel();

        Assert.Equal(PsnLoginMode.Browser, model.Mode);
        Assert.False(model.SubmitVisible);
        Assert.False(model.CanSubmit);
        Assert.True(model.BrowserVisible);
    }

    /// <summary>
    /// The Qt client's `catch` around the WebEngineView shows the paste form. A missing WebView2 is
    /// the same failure on this runtime, and it must not be a dead screen.
    /// </summary>
    [Fact]
    public void ABrowserThatWillNotCreateFallsBackToThePasteForm()
    {
        var model = new PsnLoginViewModel();
        model.FallBackToExternalBrowser();

        Assert.Equal(PsnLoginMode.Redirect, model.Mode);
        Assert.True(model.SubmitVisible);
        Assert.False(model.BrowserVisible);
        Assert.False(model.CanSubmit);          // the field is still empty
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("  https://example.invalid/?code=A  ", true)]
    public void ThePasteFormNeedsATrimmedUrl(string pasted, bool can)
    {
        var model = new PsnLoginViewModel { Mode = PsnLoginMode.Redirect, RedirectUrl = pasted };
        Assert.Equal(can, model.CanSubmit);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(" someone ", true)]
    public void TheLookupFormNeedsATrimmedOnlineId(string typed, bool can)
    {
        var model = new PsnLoginViewModel { Mode = PsnLoginMode.Username, Username = typed };
        Assert.Equal(can, model.CanSubmit);
    }

    /// <summary>A request in flight refuses a second one on either path.</summary>
    [Fact]
    public void ASubmissionInFlightRefusesASecond()
    {
        var model = new PsnLoginViewModel { Mode = PsnLoginMode.Redirect, RedirectUrl = "x" };
        Assert.True(model.CanSubmit);

        model.Submitting = true;
        Assert.False(model.CanSubmit);

        model.Mode = PsnLoginMode.Username;
        model.Username = "someone";
        Assert.False(model.CanSubmit);
    }

    /// <summary>
    /// The whole pasted URL is trimmed before the code is read out of it, because a URL copied out
    /// of a browser arrives with whitespace on it more often than not.
    /// </summary>
    [Fact]
    public void ThePastedCodeIsReadThroughTheTrim()
    {
        var model = new PsnLoginViewModel
        {
            Mode = PsnLoginMode.Redirect,
            RedirectUrl = "  " + PsnAuth.RedirectPage + "?code=ABC123\r\n",
        };

        Assert.Equal("ABC123", model.PastedCode());
    }

    /// <summary>A pasted page that is not the redirect gives no code rather than a wrong one.</summary>
    [Fact]
    public void APastedPageThatIsNotTheRedirectGivesNoCode()
    {
        var model = new PsnLoginViewModel
        {
            Mode = PsnLoginMode.Redirect,
            RedirectUrl = "https://example.invalid/?code=ABC123",
        };

        Assert.Null(model.PastedCode());
    }

    /// <summary>
    /// The finding: `onPsnLoginAccountIdError` resets `submitting` in ONE of its two branches. The
    /// paste path gets its button back so the URL can be corrected; the embedded path does not,
    /// because it has no button and offers a Retry that reloads the page instead.
    /// </summary>
    [Fact]
    public void OnlyTheBrowserlessPathGetsItsButtonBackAfterAFailure()
    {
        var browserless = new PsnLoginViewModel
        {
            Mode = PsnLoginMode.Redirect,
            RedirectUrl = "x",
            Submitting = true,
        };

        browserless.Fail("account id not found");

        Assert.False(browserless.Submitting);
        Assert.True(browserless.CanSubmit);
        Assert.True(browserless.ErrorVisible);
        Assert.Equal("account id not found", browserless.ErrorText);

        var embedded = new PsnLoginViewModel { Submitting = true };
        embedded.Fail("account id not found");

        Assert.True(embedded.Submitting);
        Assert.True(embedded.ErrorVisible);
    }

    /// <summary>Retry puts the browser back and clears what the failure said.</summary>
    [Fact]
    public void RetryClearsTheErrorAndPutsTheBrowserBack()
    {
        var model = new PsnLoginViewModel();
        model.Fail("boom");
        Assert.True(model.ErrorVisible);

        model.Retry();

        Assert.Equal(PsnLoginMode.Browser, model.Mode);
        Assert.False(model.ErrorVisible);
    }

    /// <summary>
    /// The lookup URL, and the escaping that is nearly but not quite the QML's. JavaScript's
    /// encodeURIComponent leaves !~*'() alone where .NET percent-encodes them; a PSN online id is
    /// letters, digits, hyphen and underscore, so for every id the endpoint accepts the two agree.
    /// </summary>
    [Theory]
    [InlineData("someone", "someone")]
    [InlineData("some-one_1", "some-one_1")]
    [InlineData("a b", "a%20b")]
    public void TheLookupUrlIsTheThirdPartysWithTheIdEscaped(string onlineId, string escaped)
    {
        Assert.Equal(PsnAccountLookup.SearchUrlPrefix + escaped, PsnAccountLookup.SearchUrl(onlineId));
    }

    [Fact]
    public void TheLookupReadsEncodedIdAndFallsBackToTheReplysError()
    {
        Assert.Equal("Fx1AAAAAAAA=",
            PsnAccountLookup.EncodedIdFrom(@"{""encoded_id"":""Fx1AAAAAAAA=""}"));
        Assert.Null(PsnAccountLookup.ErrorFrom(@"{""encoded_id"":""Fx1AAAAAAAA=""}"));

        Assert.Null(PsnAccountLookup.EncodedIdFrom(@"{""error"":""user not found""}"));
        Assert.Equal("user not found", PsnAccountLookup.ErrorFrom(@"{""error"":""user not found""}"));

        // An empty id is an absent one: the dialog's own test is truthiness, not presence.
        Assert.Null(PsnAccountLookup.EncodedIdFrom(@"{""encoded_id"":""""}"));
        Assert.Null(PsnAccountLookup.EncodedIdFrom("{}"));
    }

    /// <summary>Both rules, and the asymmetry, are still the QML's own.</summary>
    [Fact]
    public void TheRulesAreStillTheQmlsOwn()
    {
        if (PsnLoginSource.Locate() is null)
            return;

        string qml = File.ReadAllText(PsnLoginSource.Locate()!);

        Assert.True(PsnLoginSource.TheButtonNeedsATrimmedUrlAndNoSubmission(qml), "button rule");
        Assert.True(PsnLoginSource.TheDialogStartsWithNoButton(qml), "no button on open");
        Assert.True(PsnLoginSource.TheBrowserFailureFallsBackToTheExternalBrowser(qml), "fallback");
        Assert.True(PsnLoginSource.TheLookupIsStillTheThirdPartys(qml), "lookup");
        Assert.True(PsnLoginSource.OnlyTheBrowserlessBranchReenablesTheButton(qml), "asymmetry");
    }
}
