namespace ChiakiNg.Session;

/// <summary>Which pair of buttons the log carries, which is the only sign that it has finished.</summary>
public enum PsnTokenLogState
{
    /// <summary>Still running. The button says Cancel and Return does nothing.</summary>
    Running,

    /// <summary>Done, either way. The button says Close and Return works.</summary>
    Finished,
}

/// <summary>
/// PP15: the token dialog - the second of the two screens whose content is somebody else's page.
///
/// It shares the browser and the paste form with the login dialog (PP7) and differs in three ways
/// that a port would flatten:
///
/// 1. THE BUTTON HAS NO SUBMISSION GUARD. The login dialog's is
///    `!submitting && url.text.trim()`; this one is `url.text.trim()` alone. So this screen will
///    accept a second press while the first is still running, and the login screen will not. Both
///    are upstream's, and the difference is invisible on either screen.
///
/// 2. IT ANSWERS WITH A LOG RATHER THAN A RESULT. `initPsnAuth` calls back repeatedly with
///    (message, ok, done); the messages accumulate in a modal that cannot be dismissed by clicking
///    away, and the only thing that marks the end is its button CHANGING from Cancel to Close.
///
/// 3. IT OWNS `remotePlayAsk` FROM BOTH ENDS. Opening the screen sets it TRUE and a successful
///    setup sets it FALSE - and that flag is what PP7's <see cref="PsnBrowser.ClearsCookiesOnClose"/>
///    reads to decide whether closing the browser clears cookies. So the rule measured there has
///    its reason here: a setup that was abandoned clears the cookies behind it, and one that
///    succeeded keeps the user signed in. Neither screen states that on its own.
/// </summary>
public sealed class PsnTokenViewModel : DialogViewModel
{
    private bool expired;
    private string redirectUrl = "";
    private string loginUrl = "";
    private bool streamerMode;
    private bool logOpen;
    private string logText = "";
    private PsnTokenLogState logState = PsnTokenLogState.Running;
    private bool remotePlayAsk;

    protected override string ButtonProperty => nameof(CanSubmit);

    /// <summary>The heading when the credentials ran out rather than never existing.</summary>
    public const string ExpiredTitle = "Credentials Expired: Refresh PSN Remote Connection";

    /// <summary>And the heading for a first setup.</summary>
    public const string SetupTitle = "Setup Automatic PSN Remote Connection";

    /// <summary>The log modal's own heading, which does not change with either of those.</summary>
    public const string LogTitle = "Create PSN Automatic Remote Connection Token";

    /// <summary>Whether this screen was opened because a token expired mid-flow.</summary>
    public bool Expired
    {
        get => expired;
        set { Set(ref expired, value); Raise(nameof(Title)); }
    }

    /// <summary>The redirect URL pasted back out of a browser.</summary>
    public string RedirectUrl
    {
        get => redirectUrl;
        set => Set(ref redirectUrl, value ?? "");
    }

    /// <summary>The authorize URL offered for copying, or empty when there is none to offer.</summary>
    public string LoginUrl
    {
        get => loginUrl;
        set { Set(ref loginUrl, value ?? ""); Raise(nameof(CopyVisible)); }
    }

    /// <summary>Whether the two URL fields are masked, which is the streamer's setting.</summary>
    public bool StreamerMode
    {
        get => streamerMode;
        set => Set(ref streamerMode, value);
    }

    /// <summary>Whether the log modal is up.</summary>
    public bool LogOpen
    {
        get => logOpen;
        set => Set(ref logOpen, value);
    }

    /// <summary>Everything the callback has said so far, newline-terminated line by line.</summary>
    public string LogText
    {
        get => logText;
        private set => Set(ref logText, value);
    }

    /// <summary>Running or finished, which is the button and nothing else.</summary>
    public PsnTokenLogState LogState
    {
        get => logState;
        private set
        {
            Set(ref logState, value);
            Raise(nameof(LogButtonText));
            Raise(nameof(ReturnClosesTheLog));
        }
    }

    /// <summary>
    /// The flag this screen owns from both ends, and which PP7's browser reads on the way out.
    /// </summary>
    public bool RemotePlayAsk
    {
        get => remotePlayAsk;
        private set => Set(ref remotePlayAsk, value);
    }

    /// <summary>The heading, which is the only thing <see cref="Expired"/> changes.</summary>
    public string Title => Expired ? ExpiredTitle : SetupTitle;

    /// <summary>
    /// The button's rule: a non-blank URL after trimming, and NOTHING ELSE.
    ///
    /// The login dialog's identical-looking button also requires that no request is in flight.
    /// This one does not, so a second press lands while the first is still running. Reproduced
    /// rather than harmonised - the two screens differ upstream, and making them agree here would
    /// be a redesign wearing a bug fix's clothes.
    /// </summary>
    public bool CanSubmit => RedirectUrl.Trim().Length > 0;

    /// <summary>Whether there is a URL to copy, which is what the copy button is bound to.</summary>
    public bool CopyVisible => LoginUrl.Length > 0;

    /// <summary>Cancel while it runs, Close once it has stopped.</summary>
    public string LogButtonText => LogState == PsnTokenLogState.Running ? "Cancel" : "Close";

    /// <summary>
    /// Whether Return closes the log. Only once it is finished - the QML guards the key on the
    /// button having become Close, so Return does nothing at all while the setup is running.
    /// Escape closes either way, which is <see cref="EscapeClosesTheLog"/> and always true.
    /// </summary>
    public bool ReturnClosesTheLog => LogState == PsnTokenLogState.Finished;

    /// <summary>Escape closes it at any point, running or not.</summary>
    public static bool EscapeClosesTheLog => true;

    /// <summary>
    /// Opening the screen, which turns the ask ON. Half of the pair whose other half is a
    /// successful setup - see the class note.
    /// </summary>
    public void Opened() => RemotePlayAsk = true;

    /// <summary>Pressing Setup: the log opens before the first message arrives, not with it.</summary>
    public void Submit()
    {
        LogText = "";
        LogState = PsnTokenLogState.Running;
        LogOpen = true;
    }

    /// <summary>
    /// One call of the (message, ok, done) callback.
    ///
    /// The three arguments are independent in the signature and not in practice: the backend sends
    /// ok only together with done. Handled separately anyway, because the signature is the
    /// contract and a port that assumed the pairing would break on the day it stopped holding.
    /// </summary>
    public void Report(string message, bool ok, bool done)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (ok)
            RemotePlayAsk = false;

        LogText += message + "\n";

        if (done)
            LogState = PsnTokenLogState.Finished;
    }

    /// <summary>
    /// Closing the log, which leaves for the main view whether the setup worked or not. There is
    /// no path back to this screen from the log - `onClosed` shows the main view unconditionally.
    /// </summary>
    public bool CloseLog()
    {
        LogOpen = false;
        return true;
    }
}

/// <summary>
/// PP15: the token dialog's rules where the Qt client states them.
/// </summary>
public static class PsnTokenDialogSource
{
    /// <summary>The dialog.</summary>
    public static string? Locate() => DialogSource.Locate("PSNTokenDialog");

    /// <summary>Whether the heading still turns on one property and nothing else.</summary>
    public static bool TheTitleFollowsExpired(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains($"qsTr(\"{PsnTokenViewModel.ExpiredTitle}\")", StringComparison.Ordinal)
            && qml.Contains($"qsTr(\"{PsnTokenViewModel.SetupTitle}\")", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether this dialog's button still lacks the submission guard the login dialog's has. Both
    /// are pinned, because the finding is the difference and one of them alone does not show it.
    /// </summary>
    public static bool TheButtonHasNoSubmissionGuard(string tokenQml, string loginQml)
    {
        ArgumentNullException.ThrowIfNull(tokenQml);
        ArgumentNullException.ThrowIfNull(loginQml);

        return tokenQml.Contains("buttonEnabled: url.text.trim()", StringComparison.Ordinal)
            && !tokenQml.Contains("buttonEnabled: !submitting", StringComparison.Ordinal)
            && loginQml.Contains("buttonEnabled: !submitting && url.text.trim()", StringComparison.Ordinal);
    }

    /// <summary>Whether opening still turns the ask on and a successful setup still turns it off.</summary>
    public static bool TheAskIsSetAtBothEnds(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Chiaki.settings.remotePlayAsk = true;", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.remotePlayAsk = false;", StringComparison.Ordinal);
    }

    /// <summary>Whether the log still marks its end by changing its button rather than by a state.</summary>
    public static bool TheLogEndsByChangingItsButton(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("standardButtons: Dialog.Cancel", StringComparison.Ordinal)
            && qml.Contains("logDialog.standardButtons = Dialog.Close;", StringComparison.Ordinal);
    }

    /// <summary>Whether Return still works only once that change has happened.</summary>
    public static bool ReturnIsGuardedOnThatButton(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
            "Keys.onReturnPressed: if (logDialog.standardButtons == Dialog.Close) logDialog.close()",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the log still cannot be dismissed by clicking away from it.</summary>
    public static bool TheLogHasNoAutoClose(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("closePolicy: Popup.NoAutoClose", StringComparison.Ordinal);
    }

    /// <summary>Whether closing the log still leaves for the main view, successful or not.</summary>
    public static bool ClosingTheLogLeavesForTheMainView(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("onClosed: root.showMainView();", StringComparison.Ordinal);
    }
}
