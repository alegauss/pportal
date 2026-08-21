using ChiakiNg.Session;
using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP7: the browser's rules, asserted without a browser.
///
/// Everything the WebView2 control decides is here rather than in the control, which is what makes
/// the control a wiring file: a navigation is a string, a close is a bool, and a profile is a path.
/// </summary>
public class PsnBrowserTests
{
    /// <summary>An ordinary page of the login is loaded, and nothing is raised about it.</summary>
    [Fact]
    public void ALoginPageIsJustLoaded()
    {
        PsnNavigationDecision decision = PsnBrowser.Decide(PsnAuth.LoginUrl("duid"));

        Assert.Equal(PsnNavigation.Continue, decision.Kind);
        Assert.Null(decision.Code);
        Assert.False(decision.Cancel);
    }

    /// <summary>
    /// The redirect is stopped and its code taken. The stopping is the point: the page behind that
    /// URL is never asked for, which is what request.reject() does in the QML.
    /// </summary>
    [Fact]
    public void TheRedirectIsStoppedAndItsCodeTaken()
    {
        PsnNavigationDecision decision = PsnBrowser.Decide(PsnAuth.RedirectPage + "?code=ABC123");

        Assert.Equal(PsnNavigation.Complete, decision.Kind);
        Assert.Equal("ABC123", decision.Code);
        Assert.True(decision.Cancel);
    }

    /// <summary>
    /// And a redirect with no code is stopped just the same, without completing anything - the
    /// reject in the QML sits outside the code test, so a cancelled login does not load the page
    /// either.
    /// </summary>
    [Fact]
    public void ARedirectWithoutACodeIsStoppedAndCompletesNothing()
    {
        PsnNavigationDecision decision = PsnBrowser.Decide(PsnAuth.RedirectPage + "?error=access_denied");

        Assert.Equal(PsnNavigation.Cancelled, decision.Kind);
        Assert.Null(decision.Code);
        Assert.True(decision.Cancel);
    }

    /// <summary>A navigation to nothing is a navigation to continue, not a crash.</summary>
    [Fact]
    public void NoUrlIsNotARedirect()
        => Assert.Equal(PsnNavigation.Continue, PsnBrowser.Decide(null).Kind);

    /// <summary>
    /// The profile goes in the LOCAL application data directory and not the roaming one. This is
    /// the one place the port deliberately differs from where QtWebEngine puts it, so it is the
    /// one worth an assertion: a Chromium cache under Roaming is copied at every logon.
    /// </summary>
    [Fact]
    public void TheProfileIsLocalAndNotRoaming()
    {
        string folder = PsnBrowser.UserDataFolder;

        Assert.StartsWith(QtPaths.AppLocalDataLocation, folder, StringComparison.Ordinal);
        Assert.False(
            folder.StartsWith(QtPaths.AppDataLocation, StringComparison.Ordinal)
                && QtPaths.AppDataLocation != QtPaths.AppLocalDataLocation,
            "the browser profile must not land in the roaming root");
        Assert.EndsWith(Path.Combine("WebView2", PsnBrowser.ProfileName), folder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Closing clears the cache every time and the cookies only on the flow that asks. A port that
    /// always cleared cookies would make every login retype a password and look identical.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void OnlyTheAskingFlowClearsCookies(bool remotePlayAsk, bool expected)
        => Assert.Equal(expected, PsnBrowser.ClearsCookiesOnClose(remotePlayAsk));

    /// <summary>
    /// The user agent Qt has to rewrite, and the one WebView2 already sends. Three ways to need
    /// the spoofing and one way not to, which is why the predicate is a disjunction.
    /// </summary>
    [Theory]
    // QtWebEngine's own, which is what setWebEngineHints was written against.
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "QtWebEngine/6.8.1 Chrome/122.0.0.0 Safari/537.36", true)]
    // Windows 8 announced by a machine that is not running it.
    [InlineData(
        "Mozilla/5.0 (Windows NT 6.2; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", true)]
    // Chrome without the Edge token, which is the third rewrite.
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Safari/537.36", true)]
    // WebView2: Edge, a real version, and the right Windows. Nothing left to fake.
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0", false)]
    public void OnlyQtsBrowserNeedsItsUserAgentFaked(string userAgent, bool expected)
        => Assert.Equal(expected, PsnBrowser.NeedsHintSpoofing(userAgent));

    /// <summary>
    /// The two messages a redirect can produce, in the backend's order: not-the-redirect is
    /// decided first, so an address bar pasted from the wrong tab is told about pasting.
    /// </summary>
    [Fact]
    public void TheTwoRedirectErrorsAreTheBackendsOwn()
    {
        Assert.Equal(PsnAuth.InvalidUrlMessage, PsnAuth.RedirectError("https://example.com/?code=ABC"));
        Assert.Equal(PsnAuth.InvalidCodeMessage, PsnAuth.RedirectError(PsnAuth.RedirectPage + "?error=x"));
        Assert.Null(PsnAuth.RedirectError(PsnAuth.RedirectPage + "?code=ABC123"));
    }

    /// <summary>The dialog still rejects the redirect, keeps a named profile and hides the menu.</summary>
    [Fact]
    public void TheDialogsBrowserRulesAreStillTheQtClients()
    {
        string? file = PsnBrowserSource.Locate(PsnBrowserSource.DialogQml);
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.True(PsnBrowserSource.TheRedirectIsRejectedNotLoaded(qml), "the redirect is rejected");
        Assert.True(PsnBrowserSource.TheProfileIsPersistentAndNamed(qml), "a named, on-disk profile");
        Assert.True(PsnBrowserSource.OnlyTheAskingFlowClearsCookies(qml), "cookies only on the asking flow");
        Assert.True(PsnBrowserSource.TheContextMenuIsSuppressed(qml), "the context menu is suppressed");
    }

    /// <summary>
    /// And the spoofing this port deletes is still there to be deleted. If upstream ever stops
    /// inventing a Chrome version from a calendar, that decision deserves re-reading rather than
    /// inheriting.
    /// </summary>
    [Fact]
    public void TheSpoofingThisPortDeletesIsStillUpstream()
    {
        string? cpp = PsnBrowserSource.Locate(PsnBrowserSource.BackendCpp);
        string? header = PsnBrowserSource.Locate(PsnBrowserSource.BackendHeader);
        if (cpp is null || header is null)
            return;

        string backend = File.ReadAllText(cpp);

        Assert.True(
            PsnBrowserSource.TheChromeVersionIsInventedFromADate(backend), "a version from a date");
        Assert.True(
            PsnBrowserSource.TheUserAgentIsRewrittenThreeWays(backend), "three rewrites");
        Assert.True(
            PsnBrowserSource.EveryRequestCarriesAForgedSecChUa(File.ReadAllText(header)),
            "a forged Sec-Ch-Ua");
    }
}
