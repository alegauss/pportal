using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One place two mutexes are held at once, and in which order.</summary>
/// <param name="Where">The function, as the C names it.</param>
/// <param name="Holds">The mutex already held.</param>
/// <param name="Takes">The one acquired while holding it.</param>
/// <param name="Thread">Which thread is there.</param>
public readonly record struct LockAcquisition(
    string Where, CtrlMutex Holds, CtrlMutex Takes, string Thread);

/// <summary>
/// PP469, under PP294: both lock orders exist, so the two mutexes can deadlock.
///
/// PP468 counted the call sites and left the question open on purpose: six of seven ctrl_failed calls
/// hold notif_mutex while it takes state_mutex, one releases first, and whether that can deadlock
/// needed a sweep rather than a reading. This is the sweep, and the answer is yes.
///
/// THE SWEEP WAS BOUNDED, WHICH IS WHY IT COULD BE FINISHED. notif_mutex is acquired in exactly six
/// places, all in ctrl.c: chiaki_ctrl_init, chiaki_ctrl_stop, chiaki_ctrl_send_message,
/// chiaki_ctrl_set_login_pin, ctrl_connect_tcp and the thread func. The last two run on the ctrl
/// thread, which does not enter holding anything. So the question is only whether any of the four
/// exported ones is called with state_mutex held.
///
/// ONE IS. The session thread waits on state_cond at session.c's PIN prompt -
/// `chiaki_cond_timedwait_pred(&session->state_cond, &session->state_mutex, ...)`, which returns
/// HOLDING the mutex, as a condition variable must - and then calls chiaki_ctrl_set_login_pin, which
/// takes notif_mutex. That is state then notif.
///
/// AND THE CTRL THREAD DOES THE OPPOSITE. It holds notif_mutex across most of its loop and calls
/// ctrl_failed from inside it, which takes state_mutex. That is notif then state.
///
/// SO THE CYCLE IS COMPLETE, and both windows are real rather than instantaneous. The ctrl thread
/// holds notif_mutex from its relock after the select until the next unlock, and calls ctrl_failed
/// from four places inside that stretch; the session thread holds state_mutex from the cond wait's
/// return until it releases. A ctrl failure arriving while a user is entering a PIN is a network event
/// meeting a human one, which is the kind of coincidence that happens.
///
/// THE FIX IS NOT SHIPPED HERE. Releasing state_mutex around the PIN call is one edit and matches what
/// the two careful sites already do, but the lines after it read and free session->login_pin under that
/// same lock, so dropping it there changes what is atomic. Six edits in ctrl.c is the other direction.
/// Choosing needs more than this class knows, so this states the cycle and the fix is filed apart.
/// </summary>
public static class CtrlLockOrder
{
    /// <summary>
    /// The six places notif_mutex is acquired. All in ctrl.c, which is what bounded the sweep.
    /// </summary>
    public static IReadOnlyList<string> NotifAcquiredIn { get; } =
    [
        "chiaki_ctrl_init",
        "chiaki_ctrl_stop",
        "chiaki_ctrl_send_message",
        "chiaki_ctrl_set_login_pin",
        "ctrl_connect_tcp",
        "ctrl_thread_func",
    ];

    /// <summary>
    /// The two that run on the ctrl thread, which never arrives holding a session lock - so neither can
    /// be the second half of a cycle.
    /// </summary>
    public static IReadOnlyList<string> OnTheCtrlThread { get; } =
        ["ctrl_connect_tcp", "ctrl_thread_func"];

    /// <summary>Both orders, as found.</summary>
    public static IReadOnlyList<LockAcquisition> Acquisitions { get; } =
    [
        new(
            "chiaki_session_thread_func at the PIN prompt",
            CtrlMutex.State,
            CtrlMutex.Notif,
            "session"),

        new("ctrl_thread_func via ctrl_failed", CtrlMutex.Notif, CtrlMutex.State, "ctrl"),
    ];

    /// <summary>Whether the two acquisitions form a cycle - which is to say, whether they disagree.</summary>
    public static bool IsACycle(LockAcquisition a, LockAcquisition b)
        => a.Holds == b.Takes && a.Takes == b.Holds;

    /// <summary>Whether any pair of the found acquisitions cycles.</summary>
    public static bool ACycleExists()
    {
        for (var i = 0; i < Acquisitions.Count; i++)
        {
            for (int j = i + 1; j < Acquisitions.Count; j++)
            {
                if (IsACycle(Acquisitions[i], Acquisitions[j]))
                    return true;
            }
        }

        return false;
    }

    /// <summary>ctrl.c.</summary>
    public static string? LocateCtrl() => CtrlMessageCensus.LocateCtrl();

    /// <summary>session.c.</summary>
    public static string? LocateSession() => CtrlOnceOnly.LocateSession();

    /// <summary>
    /// Every function in ctrl.c that acquires notif_mutex, read by walking the file and remembering
    /// the last signature seen.
    ///
    /// Not through <see cref="CFunction"/>: that answers about ONE named function, and the claim here
    /// is about the whole set - that there are six and no more. A missing seventh is what would make
    /// the sweep incomplete, so the reader has to enumerate rather than confirm.
    /// </summary>
    public static IReadOnlyList<string> FunctionsTakingNotifIn(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        var found = new List<string>();
        var current = "";

        foreach (string line in ctrlSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            // A definition, not a prototype: prototypes end in a semicolon.
            if ((line.StartsWith("static ", StringComparison.Ordinal)
                    || line.StartsWith("CHIAKI_EXPORT ", StringComparison.Ordinal))
                && line.Contains('(')
                && !line.TrimEnd().EndsWith(';'))
            {
                current = NameOf(line);
            }

            if (line.Contains("chiaki_mutex_lock(&ctrl->notif_mutex", StringComparison.Ordinal)
                && current.Length > 0
                && !found.Contains(current, StringComparer.Ordinal))
            {
                found.Add(current);
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the PIN prompt still waits on state_cond and then calls into ctrl without releasing.
    ///
    /// The cond wait is what makes the lock held: it returns holding the mutex. So the assertion is the
    /// wait, then the call, with no unlock between them - which is exactly what the two careful sites
    /// in this tree do have.
    /// </summary>
    public static bool ThePinPromptStillHoldsStateAcrossTheCall(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string text = sessionSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        int waits = text.IndexOf(
            "chiaki_cond_timedwait_pred(&session->state_cond, &session->state_mutex, UINT64_MAX, session_check_state_pred_pin",
            StringComparison.Ordinal);
        if (waits < 0)
            return false;

        int call = text.IndexOf("chiaki_ctrl_set_login_pin(&session->ctrl", waits, StringComparison.Ordinal);
        if (call < 0)
            return false;

        return !text[waits..call].Contains(
            "chiaki_mutex_unlock(&session->state_mutex);", StringComparison.Ordinal);
    }

    /// <summary>And whether the PIN setter still takes notif_mutex, which closes the cycle.</summary>
    public static bool ThePinSetterStillTakesNotif(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        if (CFunction.Body(ctrlSource, "CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_set_login_pin")
            is not { } body)
        {
            return false;
        }

        return body.Contains("chiaki_mutex_lock(&ctrl->notif_mutex);", StringComparison.Ordinal);
    }

    private static string NameOf(string signature)
    {
        int open = signature.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0)
            return "";

        string before = signature[..open];
        int space = before.LastIndexOfAny([' ', '*', '\t']);

        return space >= 0 ? before[(space + 1)..].Trim() : before.Trim();
    }
}
