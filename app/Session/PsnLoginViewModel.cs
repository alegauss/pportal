using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>Which of the login screen's three shapes is on show.</summary>
public enum PsnLoginMode
{
    /// <summary>
    /// The embedded browser. Nothing is typed and there is no button - the screen is waiting for a
    /// navigation, which is the one thing this port cannot yet produce.
    /// </summary>
    Browser,

    /// <summary>
    /// The browserless path: the login URL is copied out, opened in whatever browser the machine
    /// has, and the redirect it lands on is pasted back.
    /// </summary>
    Redirect,

    /// <summary>
    /// The account id looked up by online id, which is a different endpoint and not a login at
    /// all - the dialog opens straight into it when it was asked for an id rather than a session.
    /// </summary>
    Username,
}

/// <summary>
/// PP7: the login screen minus the browser, which is two of its three paths.
///
/// PSNLoginDialog.qml is not one screen with a WebEngineView in it. It is three, and only the
/// first needs a browser: the embedded view, the paste-the-redirect-URL form the Qt client falls
/// back to when `Qt.createQmlObject` throws, and the look-up-by-online-id form the dialog opens
/// into when it was asked for an account id rather than a login. The fallback is not a Linux
/// nicety either - the `catch` around the WebEngineView calls `extBrowserButton.clicked()`, so a
/// machine with no working browser component still logs in.
///
/// That is the whole reason this file exists ahead of the WebView2 control. The two browserless
/// paths are string work and button rules, they are assertable without a browser or a PSN account,
/// and they are what the port falls back to if WebView2 is missing on the machine it lands on.
///
/// The rule worth naming is the error asymmetry. `onPsnLoginAccountIdError` resets `submitting`
/// in ONE of its two branches: the paste path re-enables its button so the URL can be corrected,
/// and the embedded path does not, because it has no button to re-enable and offers a Retry that
/// reloads the page instead. A port that reset it in both would look identical on screen and
/// leave the browser path accepting a second submission of a code already spent.
/// </summary>
public sealed class PsnLoginViewModel : DialogViewModel
{
    private PsnLoginMode mode = PsnLoginMode.Browser;
    private string redirectUrl = "";
    private string username = "";
    private string errorText = "";
    private bool submitting;

    protected override string ButtonProperty => nameof(CanSubmit);

    /// <summary>Which path is on show.</summary>
    public PsnLoginMode Mode
    {
        get => mode;
        set
        {
            Set(ref mode, value);
            Raise(nameof(SubmitVisible));
            Raise(nameof(BrowserVisible));
            Raise(nameof(RedirectVisible));
            Raise(nameof(UsernameVisible));
        }
    }

    /// <summary>The URL pasted back out of the browser.</summary>
    public string RedirectUrl
    {
        get => redirectUrl;
        set => Set(ref redirectUrl, value ?? "");
    }

    /// <summary>The online id typed into the lookup form.</summary>
    public string Username
    {
        get => username;
        set => Set(ref username, value ?? "");
    }

    /// <summary>Whether a request is in flight. Both forms refuse a second one while it is.</summary>
    public bool Submitting
    {
        get => submitting;
        set => Set(ref submitting, value);
    }

    /// <summary>The last failure, or the empty string. Shown rather than logged.</summary>
    public string ErrorText
    {
        get => errorText;
        set
        {
            Set(ref errorText, value ?? "");
            Raise(nameof(ErrorVisible));
        }
    }

    public bool ErrorVisible => ErrorText.Length > 0;

    public bool BrowserVisible => Mode == PsnLoginMode.Browser;
    public bool RedirectVisible => Mode == PsnLoginMode.Redirect;
    public bool UsernameVisible => Mode == PsnLoginMode.Username;

    /// <summary>
    /// Whether there is a button at all. The embedded path has none - the dialog declares
    /// `buttonVisible: false` and only the external-browser handler turns it on.
    /// </summary>
    public bool SubmitVisible => Mode != PsnLoginMode.Browser;

    /// <summary>
    /// The button's rule, which is the same shape on both browserless paths: something non-blank
    /// after trimming, and no request already in flight. A pasted URL arrives with whitespace on
    /// it more often than not, which is why the trim is in the rule rather than after it.
    /// </summary>
    public bool CanSubmit => Mode switch
    {
        PsnLoginMode.Redirect => !Submitting && RedirectUrl.Trim().Length > 0,
        PsnLoginMode.Username => !Submitting && Username.Trim().Length > 0,
        _ => false,
    };

    /// <summary>
    /// The code out of the pasted URL, or null when there is none - a URL that is not the redirect
    /// and a redirect the user cancelled both answer null, and <see cref="PsnAuth.IsRedirect"/>
    /// tells them apart exactly as the Qt client does.
    /// </summary>
    public string? PastedCode() => PsnAuth.CodeFrom(RedirectUrl.Trim());

    /// <summary>
    /// What the Qt client's `catch` around the WebEngineView does: give up on the embedded browser
    /// and show the paste form. Reached here when WebView2 is not on the machine, which is the same
    /// failure wearing a different runtime.
    /// </summary>
    public void FallBackToExternalBrowser()
    {
        ErrorText = "";
        Mode = PsnLoginMode.Redirect;
    }

    /// <summary>
    /// A failed lookup, with the asymmetry the QML has: the button comes back on the browserless
    /// paths so the value can be corrected, and does not on the embedded one, which offers Retry.
    /// </summary>
    public void Fail(string error)
    {
        ErrorText = error ?? "";
        if (Mode != PsnLoginMode.Browser)
            Submitting = false;
    }

    /// <summary>The embedded path's Retry: clear the error and put the browser back.</summary>
    public void Retry()
    {
        ErrorText = "";
        Mode = PsnLoginMode.Browser;
    }
}

/// <summary>
/// PP7: the account id looked up by online id, which is not Sony's endpoint.
///
/// The Qt client asks psn.flipscreen.games - a third party - and reads `encoded_id` out of the
/// reply. It is here because it is the path a user with no interest in an OAuth flow takes, and
/// because the reply's error field is the only thing that ever explains a failure: the dialog
/// prints `Error: %1!` with it and nothing else.
/// </summary>
public static class PsnAccountLookup
{
    /// <summary>The lookup, before the online id is appended.</summary>
    public const string SearchUrlPrefix = "https://psn.flipscreen.games/search.php?username=";

    /// <summary>
    /// The lookup for one online id.
    ///
    /// The QML escapes with `encodeURIComponent` and this escapes with
    /// <see cref="Uri.EscapeDataString"/>, which are NOT the same function: JavaScript leaves
    /// <c>!~*'()</c> alone and .NET percent-encodes them. It does not matter for a PSN online id,
    /// which is letters, digits, hyphen and underscore - all four unreserved in both - and it is
    /// written down because "escaped the same way" is the sort of thing a port assumes.
    /// </summary>
    public static string SearchUrl(string onlineId)
    {
        ArgumentNullException.ThrowIfNull(onlineId);
        return SearchUrlPrefix + Uri.EscapeDataString(onlineId);
    }

    /// <summary>
    /// The account id out of a reply, or null when it carries none - which is what a failure looks
    /// like, and is the same test the dialog makes before it calls its callback.
    /// </summary>
    public static string? EncodedIdFrom(string json) => Field(json, "encoded_id");

    /// <summary>The reply's own explanation for a failure, or null.</summary>
    public static string? ErrorFrom(string json) => Field(json, "error");

    private static string? Field(string json, string name)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(name, out JsonElement value))
            return null;

        // A number is accepted as well as a string: the reply is another service's, and its error
        // field is the one place a port would rather show something than throw.
        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };

        return string.IsNullOrEmpty(text) ? null : text;
    }
}

/// <summary>
/// PP7: the login dialog's two browserless paths as its QML states them.
/// </summary>
public static partial class PsnLoginSource
{
    /// <summary>The dialog, or null outside a checkout.</summary>
    public static string? Locate() => DialogSource.Locate("PSNLoginDialog");

    /// <summary>Whether the paste form's button still needs a trimmed URL and no submission.</summary>
    public static bool TheButtonNeedsATrimmedUrlAndNoSubmission(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("buttonEnabled: !submitting && url.text.trim()", StringComparison.Ordinal);
    }

    /// <summary>Whether the dialog still opens with no button, so only the paste path grows one.</summary>
    public static bool TheDialogStartsWithNoButton(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("buttonVisible: false", StringComparison.Ordinal);
    }

    /// <summary>Whether a browser that will not create still falls back to the paste form.</summary>
    public static bool TheBrowserFailureFallsBackToTheExternalBrowser(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return FallbackRegex().IsMatch(qml);
    }

    /// <summary>Whether the online-id lookup is still the third party's, escaped the same way.</summary>
    public static bool TheLookupIsStillTheThirdPartys(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
                @"""GET"", ""https://psn.flipscreen.games/search.php?username=%1"".arg(encodeURIComponent(username))",
                StringComparison.Ordinal)
            && qml.Contains(@"response[""encoded_id""]", StringComparison.Ordinal)
            && qml.Contains(@"response[""error""]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the failure handler still resets `submitting` in exactly one of its two branches.
    /// Both branches are pinned, because the finding is what one of them does NOT do.
    /// </summary>
    public static bool OnlyTheBrowserlessBranchReenablesTheButton(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return EmbeddedErrorBranchRegex().IsMatch(qml)
            && BrowserlessErrorBranchRegex().IsMatch(qml);
    }

    [GeneratedRegex(
        @"catch \(error\) \{\s*\r?\n\s*console\.error\('Create webengine view failed with error:' \+ error\);"
        + @"\s*\r?\n\s*extBrowserButton\.clicked\(\);")]
    private static partial Regex FallbackRegex();

    [GeneratedRegex(
        @"if\(nativeLoginForm\.visible\)\s*\r?\n\s*\{\s*\r?\n\s*webView\.visible = false;"
        + @"\s*\r?\n\s*nativeErrorLabel\.text = error;\s*\r?\n\s*nativeErrorLabel\.visible = true;"
        + @"\s*\r?\n\s*nativeErrorGrid\.visible = true;\s*\r?\n\s*\}")]
    private static partial Regex EmbeddedErrorBranchRegex();

    [GeneratedRegex(
        @"else\s*\r?\n\s*\{\s*\r?\n\s*errorHeader\.visible = true;\s*\r?\n\s*errorLabel\.text = error;"
        + @"\s*\r?\n\s*errorLabel\.visible = true;\s*\r?\n\s*submitting = false;")]
    private static partial Regex BrowserlessErrorBranchRegex();
}
