using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>What one navigation means to the login.</summary>
public enum PsnNavigation
{
    /// <summary>Not the redirect. Let the browser load it - this is the login itself.</summary>
    Continue,

    /// <summary>The redirect, carrying a code. Take the code and do not load the page.</summary>
    Complete,

    /// <summary>
    /// The redirect with no code on it, which is what a cancelled login looks like. Still not
    /// loaded, and not a completion either.
    /// </summary>
    Cancelled,
}

/// <summary>One navigation's outcome, and the code when there is one.</summary>
public readonly record struct PsnNavigationDecision(PsnNavigation Kind, string? Code)
{
    /// <summary>
    /// Whether the browser must be stopped. True for both halves of the redirect, because
    /// <c>request.reject()</c> in the QML does not ask whether a code was on it.
    /// </summary>
    public bool Cancel => Kind != PsnNavigation.Continue;
}

/// <summary>
/// PP7: the browser the login needs, as WebView2 rather than as a bundled Chromium.
///
/// The three rules that are not obvious
/// ------------------------------------
/// 1. The redirect is NEVER LOADED. <c>onNavigationRequested</c> calls <c>request.reject()</c>
///    the moment the URL starts with the redirect page - the code is read off the URL and the
///    navigation is stopped. A port that let it through would ask Sony's server for a page whose
///    only purpose is to carry a query string, and would race its own token exchange for a code
///    that may only be spent once.
///
/// 2. The profile is PERSISTENT. <c>offTheRecord: false</c> with <c>storageName: 'psn-token'</c>
///    is a named on-disk profile, which is why the screen has a "reload + clear cookies" button
///    at all: an incognito browser would need none. Cookies surviving is the feature - the second
///    login does not retype a password - and it is also why closing the dialog clears the HTTP
///    cache, and clears cookies too when <c>remotePlayAsk</c> says this was the flow that asks.
///
/// 3. The user-agent spoofing is DELETED, not translated. <see cref="NeedsHintSpoofing"/> is the
///    whole of why, and it is the one place this port removes code rather than moving it.
///
/// What WebView2 needs that QtWebEngine did not
/// --------------------------------------------
/// A user data folder, named here rather than defaulted. WebView2's default is a directory beside
/// the executable, and PP22 publishes this host as a single file that unpacks into a temp
/// directory - so the default would put a browser profile somewhere that is deleted, or is not
/// writable, and the failure surfaces as a login screen that never appears.
/// </summary>
public static class PsnBrowser
{
    /// <summary>The QML's <c>storageName</c>, kept so the two clients name one profile alike.</summary>
    public const string ProfileName = "psn-token";

    /// <summary>
    /// Where the browser profile goes: AppData\Local\Chiaki\Chiaki\WebView2\psn-token.
    ///
    /// LOCAL and not roaming, which is a departure from the Qt client - QtWebEngine puts a
    /// persistent profile under AppDataLocation, and that is the ROAMING root (PP3's trap 1). A
    /// Chromium profile is a cache measured in hundreds of megabytes, and a roaming profile is
    /// copied at every logon on a domain machine. The cookies this keeps are worth keeping; the
    /// cache is not worth carrying across a network.
    /// </summary>
    public static string UserDataFolder
        => Path.Combine(QtPaths.AppLocalDataLocation, "WebView2", ProfileName);

    /// <summary>
    /// What to do about one navigation, which is <c>checkPsnRedirectURL</c> and the code together.
    ///
    /// The two rejected cases are told apart rather than merged: a redirect with no code is a
    /// login the user backed out of, and the screen has something to say about that which it has
    /// nothing to say about an ordinary page load.
    /// </summary>
    public static PsnNavigationDecision Decide(string? url)
    {
        if (!PsnAuth.IsRedirect(url))
            return new PsnNavigationDecision(PsnNavigation.Continue, null);

        string? code = PsnAuth.CodeFrom(url!);
        return code is null
            ? new PsnNavigationDecision(PsnNavigation.Cancelled, null)
            : new PsnNavigationDecision(PsnNavigation.Complete, code);
    }

    /// <summary>
    /// Whether closing the dialog clears cookies as well as the cache.
    ///
    /// <c>close()</c> starts one of two timers: <c>reloadTimer</c>, which clears cookies AND the
    /// cache, when <c>remotePlayAsk</c> is set, and <c>cacheClearTimer</c>, which clears only the
    /// cache, when it is not. So the cache goes every time and the cookies go only on the flow
    /// that asks - a port that always cleared cookies would silently make every login retype a
    /// password.
    /// </summary>
    public static bool ClearsCookiesOnClose(bool remotePlayAsk) => remotePlayAsk;

    /// <summary>
    /// Whether a browser's user agent still needs what <c>setWebEngineHints</c> does to it.
    ///
    /// That function exists because QtWebEngine announces itself: it carries a QtWebEngine token,
    /// a Chromium version frozen at whatever Qt vendored, and - on some builds - "Windows NT 6.2",
    /// which is Windows 8. Sony's login page is served to browsers, so the Qt client rewrites all
    /// three and invents a Chrome version from a DATE: release 133 on 2025-02-18 and one more
    /// every 28 days, then the same number into Sec-Ch-Ua through a request interceptor. It is a
    /// clock that will eventually claim a version that does not exist.
    ///
    /// WebView2 IS Edge. Its user agent is a real Edge user agent with a real version that the OS
    /// updates, and its client hints match it, so all three rewrites are already true and the
    /// interceptor has nothing to add. Hence a predicate rather than a port: the rule is asserted
    /// on both agents, and if a future runtime ever announces itself the way Qt's did, this says
    /// so instead of the login silently being refused.
    /// </summary>
    public static bool NeedsHintSpoofing(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        return userAgent.Contains("QtWebEngine", StringComparison.Ordinal)
            || userAgent.Contains("Windows NT 6.2", StringComparison.Ordinal)
            || !userAgent.Contains(" Edg/", StringComparison.Ordinal);
    }
}

/// <summary>
/// PP7: the browser's rules where the Qt client states them.
///
/// These are drift checks and not tests of this port. Each one pins a line that a decision above
/// was read out of, so that upstream changing its mind is a red test here rather than a login
/// that stops working for a reason nobody can name.
/// </summary>
public static class PsnBrowserSource
{
    /// <summary>The dialog with the browser in it.</summary>
    public const string DialogQml = @"gui\src\qml\PSNLoginDialog.qml";

    /// <summary>Where the profile hints and the redirect check live.</summary>
    public const string BackendCpp = @"gui\src\qmlbackend.cpp";

    /// <summary>Where the Sec-Ch-Ua interceptor is declared.</summary>
    public const string BackendHeader = @"gui\include\qmlbackend.h";

    /// <summary>One of the three, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether the redirect is still rejected rather than loaded.</summary>
    public static bool TheRedirectIsRejectedNotLoaded(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if (Chiaki.checkPsnRedirectURL(request.url)) {", StringComparison.Ordinal)
            && qml.Contains("Chiaki.handlePsnLoginRedirect(request.url)", StringComparison.Ordinal)
            && qml.Contains("request.reject();", StringComparison.Ordinal);
    }

    /// <summary>Whether the profile is still a named on-disk one rather than incognito.</summary>
    public static bool TheProfileIsPersistentAndNamed(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("offTheRecord: false", StringComparison.Ordinal)
            && qml.Contains($"storageName: '{PsnBrowser.ProfileName}'", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether closing still clears the cache always and the cookies only on the asking flow.
    /// Both timers are pinned, because the finding is the difference between them.
    /// </summary>
    public static bool OnlyTheAskingFlowClearsCookies(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if(Chiaki.settings.remotePlayAsk)", StringComparison.Ordinal)
            && qml.Contains("reloadTimer.start();", StringComparison.Ordinal)
            && qml.Contains("cacheClearTimer.start();", StringComparison.Ordinal)
            && qml.Contains("Chiaki.clearCookies(webView.web.profile);", StringComparison.Ordinal);
    }

    /// <summary>Whether the context menu is still suppressed by accepting the request.</summary>
    public static bool TheContextMenuIsSuppressed(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "onContextMenuRequested: (request) => request.accepted = true;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the Chrome version is still invented from a date. The three numbers are pinned
    /// together: this is the code <see cref="PsnBrowser.NeedsHintSpoofing"/> exists to not port.
    /// </summary>
    public static bool TheChromeVersionIsInventedFromADate(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        return cpp.Contains("QDate starting_release_date(2025, 02, 18);", StringComparison.Ordinal)
            && cpp.Contains("qint64 versionsSinceStart = daysSinceStart / 28;", StringComparison.Ordinal)
            && cpp.Contains("qint64 release = 133 + versionsSinceStart;", StringComparison.Ordinal);
    }

    /// <summary>Whether the user agent is still rewritten in the three ways the predicate tests.</summary>
    public static bool TheUserAgentIsRewrittenThreeWays(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        // The backslashes are doubled in the C++ source, because the regex is written inside a
        // C string literal. Pinned as the file spells it, not as the regex reads.
        return cpp.Contains(@""" \\bQtWebEngine[^ ]*\\b""", StringComparison.Ordinal)
            && cpp.Contains(@"userAgent.replace(""Windows NT 6.2"", ""Windows NT 10.0"")", StringComparison.Ordinal)
            && cpp.Contains(@"userAgent += QString("" Edg/%1.0.0.0"").arg(chrome_version);", StringComparison.Ordinal);
    }

    /// <summary>Whether Sec-Ch-Ua is still forged on every request out of the browser.</summary>
    public static bool EveryRequestCarriesAForgedSecChUa(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Contains(@"info.setHttpHeader(""Sec-Ch-Ua""", StringComparison.Ordinal);
    }
}
