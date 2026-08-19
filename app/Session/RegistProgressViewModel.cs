using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP37's fourth example: "a dialog that stays open after the operation it was waiting on failed".
///
/// It does, and so does the one that succeeded. RegistDialog.qml's progress callback is three
/// lines and the surprising part is what is missing from them:
///
///   function(msg, ok, done) {
///       if (!done) logArea.text += msg + "\n";
///       else       logDialog.standardButtons = Dialog.Close;
///   }
///
/// `ok` is never read. Finishing does not close the dialog and does not branch on the outcome -
/// it turns the dialog's buttons into a Close, and the log stays on screen either way.
///
/// The obvious port is worse and looks better: close on success, stay open on failure. That
/// throws away the log on the one path where a user might want to read what the console said, and
/// it makes the dialog's behaviour depend on a value the client deliberately ignores. Registering
/// is the step between an installed application and a working one, and its log is the only thing
/// a person has to go on when it does not work.
///
/// The dialog also opens only if the call was accepted SYNCHRONOUSLY - registerHost returns a
/// bool before any callback arrives - so a refused request shows no dialog at all rather than an
/// empty one that never fills.
/// </summary>
public sealed class RegistProgressViewModel : INotifyPropertyChanged
{
    private readonly StringBuilder log = new();
    private bool open;
    private bool closable;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether the progress dialog is showing.</summary>
    public bool IsOpen
    {
        get => open;
        private set => Set(ref open, value, nameof(IsOpen));
    }

    /// <summary>
    /// Whether the dialog offers a Close button. False while the operation runs - the dialog is
    /// modal and there is nothing useful to do until it finishes.
    /// </summary>
    public bool IsClosable
    {
        get => closable;
        private set => Set(ref closable, value, nameof(IsClosable));
    }

    /// <summary>Everything the operation has said so far, newline-terminated per message.</summary>
    public string Log => log.ToString();

    /// <summary>
    /// Starts the operation. <paramref name="accepted"/> is what registerHost returned before any
    /// callback arrived; false means no dialog rather than an empty one.
    /// </summary>
    public void Start(bool accepted)
    {
        if (!accepted)
            return;

        // Cleared on open and not on close, which is the QML's order: logArea.text = "" happens
        // just before open(), so a second attempt does not show the first one's messages.
        log.Clear();
        IsClosable = false;
        IsOpen = true;
        Raise(nameof(Log));
    }

    /// <summary>One progress message. Appended with a newline, as the QML does.</summary>
    public void Progress(string message)
    {
        if (!IsOpen)
            return;

        log.Append(message).Append('\n');
        Raise(nameof(Log));
    }

    /// <summary>
    /// The operation finished, whether it worked or not.
    ///
    /// There is no outcome parameter, deliberately: the Qt callback takes one and never reads it,
    /// so accepting one here would invite a branch the client does not have. The dialog stays
    /// open and becomes closable, and the log is what says how it went.
    /// </summary>
    public void Finished()
    {
        if (!IsOpen)
            return;

        IsClosable = true;
    }

    /// <summary>
    /// Return, which closes it only once it is closable.
    ///
    /// `Keys.onReturnPressed: if (logDialog.standardButtons == Dialog.Close) logDialog.close()` -
    /// so pressing it during a registration does nothing at all, rather than confirming something
    /// that has not finished.
    /// </summary>
    public void Confirm()
    {
        if (IsClosable)
            IsOpen = false;
    }

    /// <summary>
    /// Escape, which closes it WHENEVER - including mid-registration.
    ///
    /// `Keys.onEscapePressed: logDialog.close()`, with no guard. That asymmetry is the rule: the
    /// dialog cannot be confirmed away early but can always be dismissed, which is what makes it
    /// modal-with-an-exit rather than a trap. A port that guarded both would leave a user watching
    /// a registration they cannot leave.
    /// </summary>
    public void Dismiss() => IsOpen = false;

    private void Set(ref bool field, bool value, string name)
    {
        if (field == value)
            return;

        field = value;
        Raise(name);
    }

    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// PP37: the progress callback as RegistDialog.qml writes it.
/// </summary>
public static partial class RegistProgressSource
{
    /// <summary>
    /// Whether finishing still only makes the dialog closable, rather than closing it.
    ///
    /// Judged from the CALLBACK's body and not from the file. The dialog does contain
    /// logDialog.close() calls - they are its key handlers - and a check that looked for the
    /// string anywhere concluded the callback closed it, which was the first version of this.
    /// </summary>
    public static bool FinishingOnlyMakesItClosable(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        Match m = CallbackRegex().Match(qml);
        return m.Success
            && m.Groups[1].Value.Contains("standardButtons = Dialog.Close", StringComparison.Ordinal)
            && !m.Groups[1].Value.Contains("close()", StringComparison.Ordinal);
    }

    /// <summary>Whether Return still closes it only when it is closable, and Escape always.</summary>
    public static bool ConfirmIsGuardedAndDismissIsNot(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains(
                "Keys.onReturnPressed: if (logDialog.standardButtons == Dialog.Close) logDialog.close()",
                StringComparison.Ordinal)
            && qml.Contains("Keys.onEscapePressed: logDialog.close()", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the callback still ignores its outcome argument.
    ///
    /// The parameter is declared and never used, which is the kind of thing a port "fixes" into a
    /// branch. Asserted by reading the callback body rather than by trusting the memory of it.
    /// </summary>
    public static bool OutcomeIsIgnored(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        Match m = CallbackRegex().Match(qml);
        return m.Success && !m.Groups[1].Value.Contains("ok", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"function\s*\(\s*msg\s*,\s*ok\s*,\s*done\s*\)\s*\{(.*?)\}\s*\)", RegexOptions.Singleline)]
    private static partial Regex CallbackRegex();

}
