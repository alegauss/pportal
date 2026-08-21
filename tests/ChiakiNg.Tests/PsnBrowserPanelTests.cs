using System.Windows;
using ChiakiNg.Session;
using ChiakiNg.Views;
using Microsoft.Web.WebView2.Core;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP7: the browser control, asserted on the machine's own terms.
///
/// What is NOT asserted here is a login. Driving one needs Sony's page, a PSN account and a
/// runtime that launches a browser process, and a test that needs all three is a test that fails
/// for reasons that have nothing to do with this code. What is asserted is everything up to that:
/// the control exists without a browser behind it, its navigation handler decides the way
/// <see cref="PsnBrowser"/> says, and the screen really does have it in the slot.
/// </summary>
public class PsnBrowserPanelTests
{
    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>
    /// The panel is constructible with no WebView2 runtime involved. That is the whole reason
    /// StartAsync is separate from the constructor: a screen that opens on the paste path must not
    /// pay for a browser, and a machine without one must reach the fallback rather than a
    /// constructor that threw.
    /// </summary>
    [Fact]
    public void ItBuildsWithoutStartingABrowser() => OnSta(() =>
    {
        var panel = new PsnBrowserPanel();

        Assert.NotNull(panel.Browser);
        Assert.Null(panel.Browser.CoreWebView2);
    });

    /// <summary>The redirect completes the login, carries the code, and stops the navigation.</summary>
    [Fact]
    public void TheRedirectCompletesAndIsNotLoaded() => OnSta(() =>
    {
        var panel = new PsnBrowserPanel();
        string? code = null;
        bool cancelled = false;

        panel.Completed += (_, value) => code = value;
        panel.LoginCancelled += (_, _) => cancelled = true;

        Assert.True(panel.OnNavigatingTo(PsnAuth.RedirectPage + "?code=ABC123"));
        Assert.Equal("ABC123", code);
        Assert.False(cancelled);
    });

    /// <summary>
    /// A redirect with no code raises the cancellation and completes nothing - and still stops the
    /// navigation, which is the half a port is most likely to get wrong.
    /// </summary>
    [Fact]
    public void ACodelessRedirectCancelsAndIsNotLoadedEither() => OnSta(() =>
    {
        var panel = new PsnBrowserPanel();
        string? code = null;
        bool cancelled = false;

        panel.Completed += (_, value) => code = value;
        panel.LoginCancelled += (_, _) => cancelled = true;

        Assert.True(panel.OnNavigatingTo(PsnAuth.RedirectPage + "?error=access_denied"));
        Assert.True(cancelled);
        Assert.Null(code);
    });

    /// <summary>The login's own pages load, and raise nothing at all.</summary>
    [Fact]
    public void TheLoginsOwnPagesLoadSilently() => OnSta(() =>
    {
        var panel = new PsnBrowserPanel();
        int raised = 0;

        panel.Completed += (_, _) => raised++;
        panel.LoginCancelled += (_, _) => raised++;

        Assert.False(panel.OnNavigatingTo(PsnAuth.LoginUrl("duid")));
        Assert.Equal(0, raised);
    });

    /// <summary>
    /// The close path's two shapes, as WebView2 spells them: the cache always, the cookies only on
    /// the flow that asks.
    /// </summary>
    [Fact]
    public void ClosingClearsTheCacheAlwaysAndCookiesOnlyWhenAsked()
    {
        Assert.Equal(
            CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.Cookies,
            PsnBrowserPanel.ClearKindsFor(remotePlayAsk: true));

        Assert.Equal(
            CoreWebView2BrowsingDataKinds.DiskCache,
            PsnBrowserPanel.ClearKindsFor(remotePlayAsk: false));
    }

    /// <summary>
    /// And the slot the screen left is filled. The panel is in BrowserHost, which is the visibility
    /// the mode already drives - so the browser appears and disappears with the path it belongs to
    /// rather than needing a rule of its own.
    /// </summary>
    [Fact]
    public void TheScreensBrowserSlotHoldsThePanel() => OnSta(() =>
    {
        var model = new PsnLoginViewModel();
        var view = new PsnLoginView { DataContext = model };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        var host = (System.Windows.Controls.Border)view.FindName("BrowserHost");
        var panel = Assert.IsType<PsnBrowserPanel>(host.Child);

        Assert.Equal(Visibility.Visible, host.Visibility);
        Assert.Null(panel.Browser.CoreWebView2);

        model.FallBackToExternalBrowser();
        view.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, host.Visibility);
    });
}
