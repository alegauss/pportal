using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the ctrl channel's two mutexes.</summary>
public enum CtrlMutex
{
    /// <summary>ctrl->notif_mutex, on the ctrl itself. Pairs with notif_pipe.</summary>
    Notif,

    /// <summary>ctrl->session->state_mutex, on the SESSION. Pairs with state_cond.</summary>
    State,
}

/// <summary>One call to ctrl_failed from the ctrl thread, and what it was holding.</summary>
/// <param name="Line">Roughly where, for a reader - not asserted, because line numbers move.</param>
/// <param name="ReleasesNotifFirst">
/// Whether the call site drops notif_mutex before calling and retakes it after.
/// </param>
/// <param name="LeavesImmediately">
/// Whether control leaves the thread's loop straight after the call - a return, or a break that lands
/// outside it. PP472: five of the six holding calls do, and one does not, which is the whole reason the
/// two fixes for PP470 are not the trade PP470 first described.
/// </param>
public readonly record struct CtrlFailedCall(
    int Line, bool ReleasesNotifFirst, bool LeavesImmediately);

/// <summary>
/// PP468, under PP294: the ctrl channel's TWO mutexes, what each guards, and the one call site that
/// treats them differently.
///
/// PP350 did this for the stop pipes - "the ctrl channel owns two stop pipes for different jobs and
/// nothing managed states which wakes what". The locks are the same shape of fact and were in the same
/// state: forty mutex operations across two mutexes in ctrl.c, three classes touching one or the other,
/// and nothing saying which guards what or that they are different objects.
///
/// THEY ARE NOT ON THE SAME THING. `notif_mutex` belongs to the CTRL and guards its own notification
/// state - the queued messages, the stop flag, the PIN and the flag that says one was entered - and it
/// pairs with `notif_pipe`. `state_mutex` belongs to the SESSION and guards the session's flags,
/// pairing with `state_cond`. A port that used one lock object for both would serialise the ctrl
/// thread against every other thread that touches session state, which is most of them.
///
/// AND THE PIN FLAG IS THE ONE WITH TWO WRITERS. PP467 established that the session flags have a sole
/// writer, so their unlocked reads are safe. `login_pin_entered` is different: the UI thread sets it in
/// `chiaki_ctrl_set_login_pin` and the ctrl thread clears it in the loop. Both do so under
/// `notif_mutex`, and PP467's argument does not carry over - this one is locked because it has to be.
///
/// SIX OF SEVEN ctrl_failed CALLS HOLD notif_mutex ACROSS IT; ONE RELEASES FIRST. ctrl_failed takes
/// `state_mutex`, so six of them hold notif and acquire state while the seventh drops notif, calls, and
/// retakes it. The session thread does the same thing the seventh does - it unlocks `state_mutex`
/// before calling into ctrl and relocks after - so there is a discipline here that one call site
/// follows and six do not.
///
/// WHETHER THAT MATTERS IS LEFT OPEN, DELIBERATELY. A deadlock needs somebody taking the two in the
/// other order, and proving nothing does is a sweep of every path into ctrl rather than a reading of
/// this file. What is written down here is the census: the count, which one is the exception, and that
/// the session thread agrees with the exception. That is what a later answer needs and what nobody
/// had.
/// </summary>
public static class CtrlMutexes
{
    /// <summary>What each mutex guards, as the C names the fields.</summary>
    public static IReadOnlyList<(CtrlMutex Mutex, string Field)> Guards { get; } =
    [
        (CtrlMutex.Notif, "login_pin_entered"),
        (CtrlMutex.Notif, "login_pin"),
        (CtrlMutex.Notif, "login_pin_size"),
        (CtrlMutex.Notif, "msg_queue"),
        (CtrlMutex.Notif, "should_stop"),
        (CtrlMutex.State, "ctrl_session_id_received"),
        (CtrlMutex.State, "stream_connection_switch_received"),
    ];

    /// <summary>How the C spells taking each one.</summary>
    public static string LockCallFor(CtrlMutex mutex) => mutex switch
    {
        CtrlMutex.Notif => "chiaki_mutex_lock(&ctrl->notif_mutex);",
        _ => "chiaki_mutex_lock(&ctrl->session->state_mutex);",
    };

    /// <summary>And what each pairs with to wake a waiter.</summary>
    public static string WakesWith(CtrlMutex mutex) => mutex switch
    {
        CtrlMutex.Notif => "notif_pipe",
        _ => "state_cond",
    };

    /// <summary>
    /// Which mutex guards a field, or null where neither does.
    /// </summary>
    public static CtrlMutex? GuardOf(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        foreach ((CtrlMutex mutex, string guarded) in Guards)
        {
            if (string.Equals(guarded, field, StringComparison.Ordinal))
                return mutex;
        }

        return null;
    }

    /// <summary>
    /// The flag with writers on two threads, which is why PP467's sole-writer argument stops here.
    /// </summary>
    public const string TheFlagWithTwoWriters = "login_pin_entered";

    /// <summary>How many ctrl_failed calls the thread makes. Seven.</summary>
    public const int CtrlFailedCalls = 7;

    /// <summary>And how many release notif_mutex around it. One.</summary>
    public const int CallsThatReleaseFirst = 1;

    /// <summary>
    /// PP472: the six that hold notif_mutex across the call, and whether control leaves the loop
    /// immediately afterwards.
    ///
    /// Five leave - a return, or a break that lands outside the loop - so nothing after them depends on
    /// notif-guarded state being unchanged, and releasing the lock around the call cannot be observed.
    /// The sixth does not: its `break` exits the `switch(message.subtype)` inside a `while(true)`, so
    /// the iteration carries on reading the rudp submessage it was part-way through.
    ///
    /// That distinction is the correction PP472 makes to PP470's own section, which described the
    /// six-edit fix as changing no cross-thread sequence. For five of the six that holds. For the sixth
    /// it is not established, and it is the one where a release would let another thread touch
    /// msg_queue or login_pin_entered mid-iteration.
    /// </summary>
    public static IReadOnlyList<CtrlFailedCall> HoldingCalls { get; } =
    [
        new(495, ReleasesNotifFirst: false, LeavesImmediately: true),   // return NULL
        new(543, ReleasesNotifFirst: false, LeavesImmediately: true),   // break, outer loop
        new(660, ReleasesNotifFirst: false, LeavesImmediately: true),   // break, outer loop
        new(667, ReleasesNotifFirst: false, LeavesImmediately: true),   // break, outer loop
        new(708, ReleasesNotifFirst: false, LeavesImmediately: false),  // break, only the switch
        new(769, ReleasesNotifFirst: false, LeavesImmediately: true),   // falls to a break
    ];

    /// <summary>How many of the holding calls leave the loop at once. Five of six.</summary>
    public static int LeaveImmediately => HoldingCalls.Count(c => c.LeavesImmediately);

    /// <summary>
    /// And the one that does not, which any fix has to treat apart.
    /// </summary>
    public static CtrlFailedCall TheOneThatCarriesOn
        => HoldingCalls.Single(c => !c.LeavesImmediately);

    /// <summary>
    /// Whether the exception's break is still inside a switch nested in a while - which is what makes
    /// it continue rather than leave.
    ///
    /// Read from the C, because the claim is about control flow and a line number is not evidence.
    /// </summary>
    public static bool TheExceptionIsStillInsideASwitchInAWhile(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string text = threadBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int loop = text.IndexOf("while(true)", StringComparison.Ordinal);
        if (loop < 0)
            return false;

        int switched = text.IndexOf("switch(message.subtype)", loop, StringComparison.Ordinal);
        if (switched < 0)
            return false;

        int call = text.IndexOf("ctrl_failed(ctrl,", switched, StringComparison.Ordinal);
        if (call < 0)
            return false;

        // No closing of the switch between it and the call, so the call is inside it.
        return !text[switched..call].Contains("\n\t\t\t\t}", StringComparison.Ordinal);
    }

    /// <summary>ctrl.c.</summary>
    public static string? LocateCtrl() => CtrlMessageCensus.LocateCtrl();

    /// <summary>The ctrl thread's body, where all seven calls are.</summary>
    public static string? ThreadBody(string ctrlSource)
        => CFunction.Body(ctrlSource, "static void *ctrl_thread_func");

    /// <summary>
    /// How many times the thread calls ctrl_failed.
    ///
    /// Through <see cref="CFunction"/>, because the thread func is forward-declared like every other
    /// static in this file - the mistake PP467 made once and this does not repeat.
    /// </summary>
    public static int CountCtrlFailedIn(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        var found = 0;
        for (int at = threadBody.IndexOf("ctrl_failed(ctrl,", StringComparison.Ordinal);
             at >= 0;
             at = threadBody.IndexOf("ctrl_failed(ctrl,", at + 1, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }

    /// <summary>
    /// How many of them are wrapped in an unlock/relock of notif_mutex.
    ///
    /// Recognised by the unlock immediately before and the lock immediately after, which is the shape
    /// the one exception has - and the shape the session thread uses for the mirror case.
    /// </summary>
    public static int CountReleasingCallsIn(string threadBody)
    {
        ArgumentNullException.ThrowIfNull(threadBody);

        string text = threadBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = text.Split('\n');
        var found = 0;

        for (var i = 1; i < lines.Length - 1; i++)
        {
            if (!lines[i].Contains("ctrl_failed(ctrl,", StringComparison.Ordinal))
                continue;

            if (lines[i - 1].Contains("chiaki_mutex_unlock(&ctrl->notif_mutex);", StringComparison.Ordinal)
                && lines[i + 1].Contains("chiaki_mutex_lock(&ctrl->notif_mutex);", StringComparison.Ordinal))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>Whether ctrl_failed still takes state_mutex, which is what makes the pairing matter.</summary>
    public static bool CtrlFailedStillTakesStateMutex(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        if (CFunction.Body(ctrlSource, "static void ctrl_failed") is not { } body)
            return false;

        return body.Contains("chiaki_mutex_lock(&ctrl->session->state_mutex)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the session thread still releases state_mutex before calling into ctrl - the mirror of
    /// the one exception, and the reason to think there is a discipline rather than an accident.
    /// </summary>
    public static bool TheSessionThreadStillReleasesBeforeCallingCtrl(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int call = text.IndexOf(
            "ctrl_message_set_fallback_session_id(&session->ctrl)", StringComparison.Ordinal);
        if (call < 0)
            return false;

        int before = text.LastIndexOf(
            "chiaki_mutex_unlock(&session->state_mutex);", call, StringComparison.Ordinal);
        int after = text.IndexOf(
            "chiaki_mutex_lock(&session->state_mutex);", call, StringComparison.Ordinal);

        if (before < 0 || after < 0)
            return false;

        // The unlock has to be the statement just before it, not one from an earlier block.
        return text[before..call].Split('\n').Length <= 2;
    }
}
