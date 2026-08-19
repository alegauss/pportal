using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// What the front door knows about a console when it decides which of its actions apply.
///
/// Not <see cref="ConsoleRow"/>, which is what the list DRAWS. The actions turn on one more thing
/// the drawing does not need - whether PSN knows a DUID for the console - and on the three flags
/// the row already carries.
/// </summary>
public readonly record struct ConsoleActionState(
    bool Discovered, bool Manual, bool Registered, string? Duid);

/// <summary>Which removal a row offers, if any.</summary>
public enum RemoveAction
{
    /// <summary>None. The row has a menu entry and it does nothing.</summary>
    None,

    /// <summary>Delete it - a console the user typed in, so it exists only because they said so.</summary>
    Delete,

    /// <summary>Hide it - a console on the network, which deleting would not remove.</summary>
    Hide,
}

/// <summary>
/// PP13: the actions on a console row, which are three questions with a wrong obvious answer each.
///
/// Connecting passes the console's NICKNAME when it was discovered and nothing when it was not.
/// The nickname is what the wake-then-connect path waits to see come back on the network, so a
/// port that always passed it would wait on a name that never arrives for a manual console, and
/// one that never passed it would connect without ever waking a console that was asleep.
///
/// Waking is offered ONLY for a console that is neither discovered nor reachable through PSN. Both
/// of the other two already have a way in - a discovered console is awake, and a DUID console is
/// reached through the relay - so a magic packet is not merely unnecessary, it is the wrong thing
/// to have on the screen.
///
/// And removing has THREE outcomes, not two. A manual console is deleted. A discovered console
/// that is not registered is hidden, because deleting a console that is on the network does not
/// remove it - it comes back on the next discovery reply. And a discovered console that IS
/// registered offers neither: the menu entry is there and it does nothing at all. That silence is
/// the branch a port fills in, and filling it in loses the user their registration.
/// </summary>
public static partial class ConsoleActions
{
    /// <summary>Whether connecting passes the console's nickname.</summary>
    public static bool ConnectSendsTheNickname(ConsoleActionState console) => console.Discovered;

    /// <summary>Whether the wake action applies at all.</summary>
    public static bool CanWake(ConsoleActionState console)
        => !console.Discovered && string.IsNullOrEmpty(console.Duid);

    /// <summary>
    /// Whether a wake would actually be sent. The screen's rule is not the backend's: the backend
    /// refuses a console it has no registration for, because a magic packet carries the registration
    /// key and there is nothing to put in it.
    /// </summary>
    public static bool WakeWouldBeSent(ConsoleActionState console)
        => CanWake(console) && console.Registered;

    /// <summary>Which removal the row offers.</summary>
    public static RemoveAction RemovalFor(ConsoleActionState console)
    {
        if (console.Manual)
            return RemoveAction.Delete;

        return console.Discovered && !console.Registered ? RemoveAction.Hide : RemoveAction.None;
    }
}

/// <summary>
/// PP13: the auto-connect screen, which is a black rectangle and two timers.
///
/// It appears when the application already knows which console to reach and is going there without
/// asking. Everything on it is about the user's way OUT, and the rules are all about timing:
///
///   for the first second and a half nothing cancels it. Escape, Ctrl+Q and a right-click are all
///   read and all ignored. That is not an oversight - a key still held down from the screen before
///   would otherwise cancel a connection the user just asked for;
///
///   cancelling does not stop anything immediately. It changes the message and starts a two-second
///   timer, so the screen says what it is doing rather than vanishing;
///
///   and a console that never woke up takes the SAME two-second exit, from a callback that is not
///   guarded by the grace period at all.
///
/// That last one is why <see cref="FailDelay"/> and <see cref="Grace"/> are next to each other
/// here. The exit runs through a stop that IS guarded, so a timeout arriving at once escapes only
/// because two seconds is longer than a second and a half. Make the grace the longer of the two
/// and a failed auto-connect leaves the user on a black screen with no way off it.
/// </summary>
public sealed class AutoConnectScreen
{
    /// <summary>How long nothing cancels it.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(1500);

    /// <summary>How long between asking to leave and leaving.</summary>
    public static readonly TimeSpan FailDelay = TimeSpan.FromMilliseconds(2000);

    /// <summary>The message it opens with.</summary>
    public const string Waiting = "Waiting for console...";

    /// <summary>The message a cancellation puts up.</summary>
    public const string Cancelling = "Cancelling connection...";

    /// <summary>The message a console that never woke puts up.</summary>
    public const string TimedOut = "Timed out waiting for console. Exiting...";

    private TimeSpan now;
    private TimeSpan? leavingAt;

    /// <summary>Whether the grace period has passed and the screen can be left.</summary>
    public bool AllowClose => now >= Grace;

    /// <summary>Whether the "press this to cancel" hint is up.</summary>
    public bool HintVisible { get; private set; }

    /// <summary>What the screen says.</summary>
    public string Message { get; private set; } = Waiting;

    /// <summary>Whether it has stopped and handed back to the console list.</summary>
    public bool Stopped { get; private set; }

    /// <summary>
    /// Moves the clock forward. Everything with a delay on it happens here, so the screen can be
    /// asserted at any point in its life without waiting through one.
    /// </summary>
    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        TimeSpan before = now;
        now += elapsed;

        if (before < Grace && now >= Grace)
            HintVisible = true;

        if (leavingAt is TimeSpan due && now >= due)
        {
            leavingAt = null;
            Stop();
        }
    }

    /// <summary>Escape, Ctrl+Q or a right-click. Ignored entirely during the grace period.</summary>
    public void Cancel()
    {
        if (!AllowClose)
            return;

        HintVisible = false;
        Message = Cancelling;
        leavingAt = now + FailDelay;
    }

    /// <summary>
    /// The console never came back. NOT guarded - it can arrive during the grace period, and the
    /// exit it schedules is what the grace period then has to be shorter than.
    /// </summary>
    public void WakeupFailed()
    {
        HintVisible = false;
        Message = TimedOut;
        leavingAt = now + FailDelay;
    }

    /// <summary>
    /// Leaves. The guard is Qt's and it never fires: every exit is scheduled two seconds out and
    /// the grace period is a second and a half, so the clock has always passed it by the time this
    /// runs. Kept because it is what the source says, and because it is the reason the two
    /// intervals cannot be changed independently of each other.
    /// </summary>
    private void Stop()
    {
        if (!AllowClose)
            return;

        Stopped = true;
    }

    /// <summary>
    /// What the hint says, which depends on what the user has in their hands. A controller gets a
    /// button name and a keyboard gets two words, because "press Circle" is unanswerable on a
    /// machine with no pad attached.
    /// </summary>
    public static string CancelHint(bool hasController, bool isDeck)
        => !hasController ? "escape or right-click" : isDeck ? "B" : "Circle";
}

/// <summary>
/// PP13: the front door's actions and its auto-connect screen, held against the QML.
/// </summary>
public static partial class FrontDoorSource
{
    /// <summary>The console list.</summary>
    public const string MainViewQml = @"gui\src\qml\MainView.qml";

    /// <summary>The auto-connect screen.</summary>
    public const string AutoConnectQml = @"gui\src\qml\AutoConnectView.qml";

    /// <summary>One of the two, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>Whether connecting still passes the nickname only for a discovered console.</summary>
    public static bool TheNicknameGoesOnlyWithADiscoveredConsole(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return NicknameRegex().IsMatch(qml);
    }

    /// <summary>Whether waking is still refused for a discovered console and for a DUID one.</summary>
    public static bool WakingNeedsNeitherDiscoveryNorADuid(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("if(!modelData.discovered && !modelData.duid)", StringComparison.Ordinal);
    }

    /// <summary>Whether a discovered, registered console is still offered neither removal.</summary>
    public static bool RemovingHasThreeOutcomes(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return RemovalRegex().IsMatch(qml);
    }

    /// <summary>The two intervals the auto-connect screen declares, in the order it declares them.</summary>
    public static IReadOnlyList<int> Intervals(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return [.. IntervalRegex().Matches(qml).Select(m => int.Parse(m.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>Whether leaving is still guarded where the timeout that schedules it is not.</summary>
    public static bool StopIsGuardedAndTheTimeoutIsNot(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return StopGuardRegex().IsMatch(qml) && TimeoutUnguardedRegex().IsMatch(qml);
    }

    [GeneratedRegex(
        @"if\(modelData\.discovered\)\s*\r?\n\s*Chiaki\.connectToHost\(index, modelData\.name\);\s*\r?\n\s*else\s*\r?\n\s*Chiaki\.connectToHost\(index\);")]
    private static partial Regex NicknameRegex();

    [GeneratedRegex(
        @"if \(modelData\.manual\)[\s\S]{0,400}?else if \(modelData\.discovered && !modelData\.registered\)")]
    private static partial Regex RemovalRegex();

    [GeneratedRegex(@"interval: (\d+)")]
    private static partial Regex IntervalRegex();

    [GeneratedRegex(@"function stop\(\) \{\s*\r?\n\s*if \(!allowClose\)\s*\r?\n\s*return;")]
    private static partial Regex StopGuardRegex();

    [GeneratedRegex(
        @"function onWakeupStartFailed\(\) \{\s*\r?\n\s*view\.textVisible = false;")]
    private static partial Regex TimeoutUnguardedRegex();
}
