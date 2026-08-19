using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>What a dismissal of the host did, in the order the host does it.</summary>
public enum DialogHostExit
{
    /// <summary>Still open.</summary>
    None,

    /// <summary>The back button: rejected AND closed, in that order.</summary>
    Back,

    /// <summary>Escape: closed and NOT rejected, which is not the same thing.</summary>
    Escape,

    /// <summary>The action button: accepted, and NOT closed.</summary>
    Accepted,
}

/// <summary>
/// PP19: DialogView, the host the other four dialogs open inside - the last of the tail and the one
/// that owns the chrome they all share.
///
/// A title bar with a back button, one configurable action button, and a content area the screen
/// reparents itself into. Three of its rules are asymmetries a port would tidy away:
///
///   the back button REJECTS and then CLOSES. The action button only ACCEPTS - it does not close, so
///   the screen's own handler decides whether to. PP141 is why that matters: the registration dialog
///   stays open on both outcomes, which is only possible because accepting does not dismiss;
///
///   Escape CLOSES WITHOUT REJECTING. It is not the back button with a keyboard on it - back fires
///   `rejected()` and Escape does not, so a port wiring Escape to the back handler runs reject
///   callbacks the Qt client never runs;
///
///   and the Menu key presses the action button THROUGH its enabled state. So the keyboard path
///   respects whatever rule the screen put on the button, rather than bypassing it.
///
/// The focus handover is the fourth, and it is the opposite convention to the confirm dialog's.
/// Deactivating saves the focused item; activating restores it AND CLEARS the saved one, so the
/// restore is one-shot and a second activation falls back to the content's first focusable item.
/// ConfirmDialog's latch is never cleared and this one always is - two screens, two conventions, for
/// the same problem.
/// </summary>
public sealed class DialogHostViewModel : DialogViewModel
{
    private string title = "";
    private string header = "";
    private string buttonText = "";
    private bool buttonEnabled;
    private bool buttonVisible = true;
    private object? restoreFocusItem;

    protected override string ButtonProperty => nameof(ButtonEnabled);

    /// <summary>The centred bold title.</summary>
    public string Title
    {
        get => title;
        set => Set(ref title, value ?? "");
    }

    /// <summary>The smaller note beside it, which most screens leave empty.</summary>
    public string Header
    {
        get => header;
        set => Set(ref header, value ?? "");
    }

    /// <summary>The action button's label, which the screen supplies.</summary>
    public string ButtonText
    {
        get => buttonText;
        set => Set(ref buttonText, value ?? "");
    }

    /// <summary>Whether the action button can be pressed. The screen's rule, not the host's.</summary>
    public bool ButtonEnabled
    {
        get => buttonEnabled;
        set => Set(ref buttonEnabled, value);
    }

    /// <summary>
    /// Whether it is there at all. Several screens open with it hidden and turn it on later, which
    /// is why this is a separate property from <see cref="ButtonEnabled"/>.
    /// </summary>
    public bool ButtonVisible
    {
        get => buttonVisible;
        set => Set(ref buttonVisible, value);
    }

    /// <summary>How the host was left.</summary>
    public DialogHostExit Exit { get; private set; }

    /// <summary>Whether `rejected` was raised. Back does; Escape does not.</summary>
    public bool Rejected { get; private set; }

    /// <summary>Whether `accepted` was raised.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Whether the host closed. Accepting does NOT close it.</summary>
    public bool Closed { get; private set; }

    /// <summary>The saved focus target, or null once it has been spent.</summary>
    public object? RestoreFocusItem => restoreFocusItem;

    /// <summary>
    /// Whether the next activation will fall back to the content's first focusable item rather than
    /// restoring - which is what an empty save slot means.
    /// </summary>
    public bool WillFallBackToTheFocusChain => restoreFocusItem is null;

    /// <summary>The host being pushed under something else: remember who had focus.</summary>
    public void Deactivating(object? activeFocusItem) => restoreFocusItem = activeFocusItem;

    /// <summary>
    /// The host coming back. Returns what should take focus: the saved item once, then the
    /// content's chain for ever after, because the restore CLEARS the slot.
    /// </summary>
    public object? Activated(object? firstInContentChain)
    {
        if (restoreFocusItem is null)
            return firstInContentChain;

        object? target = restoreFocusItem;
        restoreFocusItem = null;
        return target;
    }

    /// <summary>The back button: reject, then close.</summary>
    public void Back()
    {
        Rejected = true;
        Closed = true;
        Exit = DialogHostExit.Back;
    }

    /// <summary>
    /// Escape: close, and do NOT reject. The difference is a reject callback that does or does not
    /// run, and nothing on screen distinguishes the two dismissals.
    /// </summary>
    public void Escape()
    {
        Closed = true;
        Exit = DialogHostExit.Escape;
    }

    /// <summary>The action button: accept, and do not close.</summary>
    public void Accept()
    {
        Accepted = true;
        Exit = DialogHostExit.Accepted;
    }

    /// <summary>
    /// The Menu key, which presses the action button through its enabled state - so a screen whose
    /// rule refuses the press refuses it from the keyboard too.
    /// </summary>
    public bool MenuKey()
    {
        if (!ButtonEnabled)
            return false;

        Accept();
        return true;
    }
}

/// <summary>
/// PP19: the host's rules where the QML states them.
/// </summary>
public static partial class DialogHostSource
{
    /// <summary>The host, or null outside a checkout.</summary>
    public static string? Locate() => DialogSource.Locate("DialogView");

    /// <summary>Whether the back button still rejects before it closes.</summary>
    public static bool BackRejectsThenCloses(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return BackRegex().IsMatch(qml);
    }

    /// <summary>Whether Escape still closes without rejecting.</summary>
    public static bool EscapeClosesWithoutRejecting(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Keys.onEscapePressed: close()", StringComparison.Ordinal)
            && !EscapeRejectRegex().IsMatch(qml);
    }

    /// <summary>Whether the action button still only accepts, leaving the closing to the screen.</summary>
    public static bool AcceptDoesNotClose(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("onClicked: dialog.accepted()", StringComparison.Ordinal)
            && !AcceptThenCloseRegex().IsMatch(qml);
    }

    /// <summary>Whether the Menu key still goes through the button's enabled state.</summary>
    public static bool TheMenuKeyRespectsTheButton(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return MenuRegex().IsMatch(qml);
    }

    /// <summary>Whether the focus restore is still one-shot - the saved item nulled after use.</summary>
    public static bool TheFocusRestoreIsOneShot(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("restoreFocusItem = Window.window.activeFocusItem;", StringComparison.Ordinal)
            && OneShotRegex().IsMatch(qml);
    }

    /// <summary>Whether the fallback is still the content's next item in the focus chain.</summary>
    public static bool TheFallbackIsTheContentChain(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("let item = mainItem.nextItemInFocusChain();", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"onClicked: \{\s*\r?\n\s*dialog\.rejected\(\);\s*\r?\n\s*dialog\.close\(\);\s*\r?\n\s*\}")]
    private static partial Regex BackRegex();

    [GeneratedRegex(@"Keys\.onEscapePressed:[^\r\n]*rejected")]
    private static partial Regex EscapeRejectRegex();

    [GeneratedRegex(@"dialog\.accepted\(\);\s*\r?\n\s*dialog\.close\(\)")]
    private static partial Regex AcceptThenCloseRegex();

    [GeneratedRegex(
        @"Keys\.onMenuPressed: \{\s*\r?\n\s*if \(okButton\.enabled\)\s*\r?\n\s*okButton\.clicked\(\)")]
    private static partial Regex MenuRegex();

    [GeneratedRegex(
        @"restoreFocusItem\.forceActiveFocus\(Qt\.TabFocusReason\);\s*\r?\n\s*restoreFocusItem = null;")]
    private static partial Regex OneShotRegex();
}
