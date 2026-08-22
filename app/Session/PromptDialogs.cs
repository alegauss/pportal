using System.Text.RegularExpressions;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>
/// PP19: which of a dialog's THREE exits was taken.
///
/// Qt's Dialog has accept, reject and close, and the remind dialogs use all three for different
/// answers - so this is not a bool. "Later" is the one a port loses: it is the exit that writes
/// nothing, which is what makes the prompt come back.
/// </summary>
public enum PromptOutcome
{
    /// <summary>Still open.</summary>
    None,

    /// <summary>Yes. Runs the callback.</summary>
    Accepted,

    /// <summary>No. Writes the "stop asking" preference.</summary>
    Declined,

    /// <summary>Remind me later. Writes nothing, so the prompt returns next run.</summary>
    Later,
}

/// <summary>
/// PP19: the confirm dialog, whose interesting part is a flag that does not mean what it says.
///
/// `newDialogOpen` reads like "a new dialog is open". What it actually does is stop
/// <c>onClosed</c> restoring focus a second time, because <c>onAccepted</c> already did. Every path
/// restores focus exactly once:
///
///   accept restores it and sets the flag, so the close that follows does not;
///   reject WITH a reject callback does the same;
///   reject WITHOUT one leaves the flag alone, so the close is what restores it.
///
/// A port that named the flag what it says - and therefore only set it when it really was opening
/// something - would restore focus twice on accept, which moves focus off whatever the callback
/// just opened.
///
/// It is also never set back to false. A dialog instance reused after an accept carries the latch
/// into its next close, so the second dismissal restores nothing. Reproduced, not fixed: the QML
/// creates these per use, so it is a defect only for a port that pools them - and knowing that is
/// the reason to write it down rather than quietly reset it.
/// </summary>
public sealed class ConfirmDialogViewModel : DialogViewModel
{
    private bool focusRestored;
    private int restoreCount;

    protected override string ButtonProperty => nameof(CanSubmit);

    /// <summary>The question. There is no rule about it - both buttons are always enabled.</summary>
    public string Text { get; set; } = "";

    /// <summary>Whether a reject callback was supplied, which changes which exit restores focus.</summary>
    public bool HasRejectCallback { get; init; }

    /// <summary>How the dialog was dismissed.</summary>
    public PromptOutcome Outcome { get; private set; }

    /// <summary>Both buttons are live from the moment it opens; there is nothing to validate.</summary>
    public bool CanSubmit => Outcome == PromptOutcome.None;

    /// <summary>How many times focus has been handed back. One, on every path.</summary>
    public int RestoreCount => restoreCount;

    /// <summary>The latch. Named for what it does rather than for what the QML calls it.</summary>
    public bool FocusAlreadyRestored => focusRestored;

    /// <summary>Yes.</summary>
    public void Accept()
    {
        Outcome = PromptOutcome.Accepted;
        focusRestored = true;
        RestoreFocus();
        Raise(nameof(CanSubmit));
    }

    /// <summary>No. Restores focus here only when there is a callback to hand it to.</summary>
    public void Reject()
    {
        Outcome = PromptOutcome.Declined;
        if (HasRejectCallback)
        {
            focusRestored = true;
            RestoreFocus();
        }

        Raise(nameof(CanSubmit));
    }

    /// <summary>
    /// The close that follows either button, or a dismissal that is neither. Restores focus unless
    /// something already did.
    /// </summary>
    public void Closed()
    {
        if (focusRestored)
            return;

        RestoreFocus();
    }

    private void RestoreFocus() => restoreCount++;
}

/// <summary>What the remind dialog's closure asks the shell to do next.</summary>
public enum RemindFollowUp
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Open the PSN prompt, because the tokens are not all there.</summary>
    ShowPsnPrompt,

    /// <summary>Stop asking about remote play - the tokens are already present.</summary>
    ClearRemotePlayAsk,
}

/// <summary>
/// PP19: the remind dialog, which is one screen doing two prompts and chaining into a third.
///
/// Three buttons and three different meanings, and they are not three variations of yes:
///
///   Yes accepts and runs the callback;
///   No REJECTS, which writes the "stop asking" preference - remotePlayAsk for the remote-play
///   prompt and addSteamShortcutAsk for the other, one flag choosing between two keys;
///   Remind Me Later CLOSES without accepting or rejecting, so nothing is written and the prompt
///   comes back next run. A port that modelled the answer as a bool would have to invent this one,
///   and inventing it as "no" turns a deferral into a permanent refusal.
///
/// The chain is the second finding. When the STEAM prompt closes without being accepted, its
/// handler looks at remote play: if the PSN tokens are not all present it opens the remote-play
/// prompt, and if they are it clears remotePlayAsk instead. So the second prompt is triggered by
/// the first one's dismissal, and accepting the first suppresses it entirely - the same
/// already-restored latch as the confirm dialog guards the early return.
///
/// And a keyboard oddity, reproduced: the Yes KEY closes rather than accepts. `Keys.onYesPressed`
/// is bound to `dialog.close()` while the Yes BUTTON is bound to `accept()`, so pressing the key
/// means "later" and clicking the button means "yes".
/// </summary>
public sealed class RemindDialogViewModel : DialogViewModel
{
    /// <summary>The preference the remote-play prompt stops itself with.</summary>
    public const string RemotePlayAskKey = "settings/remote_play_ask";

    /// <summary>The preference the Steam-shortcut prompt stops itself with.</summary>
    public const string SteamShortcutAskKey = "settings/add_steam_shortcut_ask";

    /// <summary>The four values that together mean "PSN is already linked".</summary>
    public static IReadOnlyList<string> PsnTokenKeys { get; } =
    [
        "settings/psn_refresh_token",
        "settings/psn_auth_token",
        "settings/psn_auth_token_expiry",
        "settings/psn_account_id",
    ];

    private bool focusRestored;
    private int restoreCount;

    /// <summary>Which prompt this is. It decides the key, not the layout.</summary>
    public bool IsRemotePlay { get; init; }

    /// <summary>The prompt text.</summary>
    public string Text { get; set; } = "";

    /// <summary>How it was dismissed.</summary>
    public PromptOutcome Outcome { get; private set; }

    /// <summary>Whatever the closure asked for, after <see cref="Closed"/>.</summary>
    public RemindFollowUp FollowUp { get; private set; }

    /// <summary>All three buttons are live until it closes.</summary>
    public bool CanSubmit => Outcome == PromptOutcome.None;

    /// <summary>One, on every path.</summary>
    public int RestoreCount => restoreCount;

    /// <summary>The preference this prompt writes when it is declined, and nothing else writes.</summary>
    public string AskKey => IsRemotePlay ? RemotePlayAskKey : SteamShortcutAskKey;

    /// <summary>The key set to false by a decline, or null where a decline writes nothing.</summary>
    public string? DeclinedKey { get; private set; }

    protected override string ButtonProperty => nameof(CanSubmit);

    /// <summary>Whether the four PSN values are all present.</summary>
    public static bool PsnIsLinked(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return PsnTokenKeys.All(key => !string.IsNullOrEmpty(preferences.GetString(key)));
    }

    /// <summary>Yes.</summary>
    public void Accept()
    {
        Outcome = PromptOutcome.Accepted;
        focusRestored = true;
        restoreCount++;
        Raise(nameof(CanSubmit));
    }

    /// <summary>No - which is what writes the preference, and the only thing that does.</summary>
    public void Reject()
    {
        Outcome = PromptOutcome.Declined;
        DeclinedKey = AskKey;
        Raise(nameof(CanSubmit));
    }

    /// <summary>
    /// Remind me later: neither accept nor reject, so nothing is written and the prompt returns.
    /// The Yes KEY lands here too, which the QML binds and this reproduces.
    /// </summary>
    public void Later()
    {
        Outcome = PromptOutcome.Later;
        Raise(nameof(CanSubmit));
    }

    /// <summary>
    /// The closure, which follows every exit and is where the chain lives.
    ///
    /// Returns early on the latch, so accepting suppresses both the focus restore and the chain.
    /// The chain only ever runs off the STEAM prompt: the remote-play one does not ask about
    /// itself.
    /// </summary>
    public void Closed(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (focusRestored)
            return;

        restoreCount++;

        if (IsRemotePlay || !preferences.GetBool(RemotePlayAskKey))
            return;

        FollowUp = PsnIsLinked(preferences)
            ? RemindFollowUp.ClearRemotePlayAsk
            : RemindFollowUp.ShowPsnPrompt;
    }
}

/// <summary>
/// PP19: the two prompts' rules where the QML states them.
/// </summary>
public static partial class PromptDialogSource
{
    /// <summary>The confirm dialog.</summary>
    public const string ConfirmDialog = "ConfirmDialog";

    /// <summary>The remind dialog.</summary>
    public const string RemindDialog = "RemindDialog";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string dialog) => DialogSource.Locate(dialog);

    /// <summary>Whether accept still restores focus and sets the latch, in that order.</summary>
    public static bool AcceptRestoresFocusUnderTheLatch(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return AcceptRegex().IsMatch(qml);
    }

    /// <summary>Whether the close still returns early on the latch rather than restoring again.</summary>
    public static bool CloseIsGuardedByTheLatch(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("onClosed: if(!newDialogOpen) { restoreFocus() }", StringComparison.Ordinal)
            || ClosedGuardRegex().IsMatch(qml);
    }

    /// <summary>Whether the latch is still never reset, which is what makes a reused instance stale.</summary>
    public static bool TheLatchIsNeverCleared(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        // PP272: the latch has to exist before "never cleared" is a statement about it.
        return qml.Contains("newDialogOpen", StringComparison.Ordinal)
            && !qml.Contains("newDialogOpen = false", StringComparison.Ordinal);
    }

    /// <summary>Whether reject still restores focus only when a reject callback was supplied.</summary>
    public static bool RejectRestoresOnlyWithACallback(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return RejectRegex().IsMatch(qml);
    }

    /// <summary>Whether declining still picks between the two ask preferences on one flag.</summary>
    public static bool DeclinePicksBetweenTwoKeys(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return DeclineRegex().IsMatch(qml);
    }

    /// <summary>Whether Remind Me Later is still a close rather than a reject.</summary>
    public static bool LaterIsAClose(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(@"text: qsTr(""Remind Me Later"")", StringComparison.Ordinal)
            && LaterRegex().IsMatch(qml);
    }

    /// <summary>Whether the Yes KEY still closes where the Yes BUTTON accepts.</summary>
    public static bool TheYesKeyClosesRatherThanAccepting(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Keys.onYesPressed: dialog.close()", StringComparison.Ordinal)
            && qml.Contains("Keys.onReturnPressed: dialog.accept()", StringComparison.Ordinal);
    }

    /// <summary>Whether the closure still chains into the remote-play prompt, tokens permitting.</summary>
    public static bool CloseChainsIntoThePsnPrompt(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if(!remotePlay && Chiaki.settings.remotePlayAsk)", StringComparison.Ordinal)
            && qml.Contains("root.showRemindDialog(", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.remotePlayAsk = false;", StringComparison.Ordinal);
    }

    /// <summary>The four settings the chain treats as "PSN is linked", as the QML names them.</summary>
    public static bool ThePsnCheckIsTheseFour(string qml, IEnumerable<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(qml);
        ArgumentNullException.ThrowIfNull(propertyNames);

        return propertyNames.All(
            name => qml.Contains($"!Chiaki.settings.{name}", StringComparison.Ordinal));
    }

    [GeneratedRegex(
        @"onAccepted: \{\s*\r?\n\s*newDialogOpen = true;\s*\r?\n\s*restoreFocus\(\);\s*\r?\n\s*callback\(\);")]
    private static partial Regex AcceptRegex();

    [GeneratedRegex(
        @"onClosed: \{\s*\r?\n\s*if\(newDialogOpen\)\s*\r?\n\s*return;")]
    private static partial Regex ClosedGuardRegex();

    [GeneratedRegex(
        @"onRejected: \{\s*\r?\n\s*if\(rejectCallback\)\s*\r?\n\s*\{\s*\r?\n\s*newDialogOpen = true;"
        + @"\s*\r?\n\s*restoreFocus\(\);\s*\r?\n\s*rejectCallback\(\);")]
    private static partial Regex RejectRegex();

    [GeneratedRegex(
        @"if\(dialog\.remotePlay\)\s*\r?\n\s*Chiaki\.settings\.remotePlayAsk = false;\s*\r?\n\s*else"
        + @"\s*\r?\n\s*Chiaki\.settings\.addSteamShortcutAsk = false;")]
    private static partial Regex DeclineRegex();

    [GeneratedRegex(@"text: qsTr\(""Remind Me Later""\)[\s\S]{0,120}?onClicked: dialog\.close\(\)")]
    private static partial Regex LaterRegex();
}
