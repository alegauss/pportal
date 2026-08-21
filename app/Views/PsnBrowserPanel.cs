using System.Windows.Controls;
using ChiakiNg.Session;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChiakiNg.Views;

/// <summary>
/// PP7: the embedded browser, filling the slot PsnLoginView left for it.
///
/// A control and not code-behind, for one reason: everything here that has a rule in it is
/// reachable without a browser. <see cref="OnNavigatingTo"/> is the whole of the navigation
/// handler and takes a string; <see cref="ClearKindsFor"/> is the whole of the close path and
/// takes a bool. What is left touching WebView2 is <see cref="StartAsync"/> - create the
/// environment, hand it to the control, point it at the URL - and that part has no decision in it
/// beyond where it puts its profile, which <see cref="PsnBrowser.UserDataFolder"/> already states.
///
/// The failure that matters is a runtime that is not there. WebView2 is part of Windows 11 and is
/// not part of every Windows 10, so <see cref="StartAsync"/> catches rather than throws and raises
/// <see cref="Unavailable"/> - which is the same event the Qt client's <c>catch</c> around
/// <c>Qt.createQmlObject</c> produces, and the screen answers it the same way, by falling back to
/// the paste form.
/// </summary>
public sealed class PsnBrowserPanel : ContentControl
{
    private readonly WebView2 browser = new();

    /// <summary>The login finished: the code off the redirect, which was never loaded.</summary>
    public event EventHandler<string>? Completed;

    /// <summary>The redirect arrived carrying no code, which is a login backed out of.</summary>
    public event EventHandler? LoginCancelled;

    /// <summary>
    /// There is no usable browser. Carries the failure rather than swallowing it, because
    /// "WebView2 is missing" and "the profile directory is not writable" are the same screen and
    /// very different fixes.
    /// </summary>
    public event EventHandler<Exception>? Unavailable;

    public PsnBrowserPanel()
    {
        Content = browser;
        browser.NavigationStarting += (_, e) => e.Cancel = OnNavigatingTo(e.Uri);
    }

    /// <summary>The control itself, for a caller that has to size or focus it.</summary>
    public WebView2 Browser => browser;

    /// <summary>
    /// What the navigation handler does, minus the browser: decide, raise, and answer whether the
    /// navigation is to be stopped.
    ///
    /// Both rejected cases stop the navigation and only one of them completes a login. That is
    /// <c>request.reject()</c> sitting outside the code test in the QML, and it is the difference
    /// between a cancelled login and a login that quietly asks Sony for a page it does not need.
    /// </summary>
    public bool OnNavigatingTo(string? url)
    {
        PsnNavigationDecision decision = PsnBrowser.Decide(url);

        switch (decision.Kind)
        {
            case PsnNavigation.Complete:
                Completed?.Invoke(this, decision.Code!);
                break;
            case PsnNavigation.Cancelled:
                LoginCancelled?.Invoke(this, EventArgs.Empty);
                break;
        }

        return decision.Cancel;
    }

    /// <summary>
    /// What closing clears: the disk cache always, the cookies only on the flow that asks. The
    /// QML's two timers as one expression - see <see cref="PsnBrowser.ClearsCookiesOnClose"/>.
    /// </summary>
    public static CoreWebView2BrowsingDataKinds ClearKindsFor(bool remotePlayAsk)
        => PsnBrowser.ClearsCookiesOnClose(remotePlayAsk)
            ? CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.Cookies
            : CoreWebView2BrowsingDataKinds.DiskCache;

    /// <summary>
    /// Bring the browser up on the login URL, or say there is no browser.
    ///
    /// The environment is created with an explicit user data folder because WebView2's default is
    /// beside the executable, which under PP22's single-file publish is a temp directory that is
    /// unpacked fresh - a persistent profile put there is not persistent.
    /// </summary>
    public async Task StartAsync(string loginUrl)
    {
        ArgumentNullException.ThrowIfNull(loginUrl);

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment
                .CreateAsync(null, PsnBrowser.UserDataFolder)
                .ConfigureAwait(true);

            await browser.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

            // onContextMenuRequested: (request) => request.accepted = true - the page is Sony's
            // and a context menu on it offers a user nothing this screen can honour.
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            browser.CoreWebView2.Navigate(loginUrl);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Unavailable?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Clear what the close path clears, and answer whether anything was cleared - false when
    /// there is no browser to clear, which is the ordinary case on the paste path.
    /// </summary>
    public async Task<bool> ClearOnCloseAsync(bool remotePlayAsk)
    {
        if (browser.CoreWebView2 is null)
            return false;

        await browser.CoreWebView2.Profile
            .ClearBrowsingDataAsync(ClearKindsFor(remotePlayAsk))
            .ConfigureAwait(true);

        return true;
    }
}
