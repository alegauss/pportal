using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which arm of chiaki_session_init's fork a session takes.</summary>
public enum SessionArm
{
    /// <summary>No holepunch session: the address is resolved and the caller's keys are copied.</summary>
    Local,

    /// <summary>A holepunch session: the account id is copied and the keys come later.</summary>
    Psn,
}

/// <summary>How the wait for a PSN registration ended.</summary>
public enum RegistWaitOutcome
{
    /// <summary>The callback wrote the keys and set psn_regist_succeeded.</summary>
    Succeeded,

    /// <summary>CHIAKI_REGIST_EVENT_TYPE_FINISHED_CANCELED. A quit reason, and should_stop.</summary>
    Canceled,

    /// <summary>CHIAKI_REGIST_EVENT_TYPE_FINISHED_FAILED. The same two.</summary>
    Failed,

    /// <summary>Ten seconds passed and the predicate never came true. Nothing was set.</summary>
    TimedOut,
}

/// <summary>What the session holds after the fork and the wait.</summary>
/// <param name="Arm">Which arm ran.</param>
/// <param name="AddressResolved">Whether getaddrinfo ran for this session.</param>
/// <param name="KeysFromCaller">Whether regist_key and morning came from the caller.</param>
/// <param name="KeysFromConsole">Whether the regist callback wrote them.</param>
/// <param name="HasRegistKey">Whether a key is present at all when the request is built.</param>
/// <param name="Stops">Whether CHECK_STOP ends the thread before the session request.</param>
/// <param name="QuitReason">The reason set, where one was.</param>
public readonly record struct SessionRegistOutcome(
    SessionArm Arm,
    bool AddressResolved,
    bool KeysFromCaller,
    bool KeysFromConsole,
    bool HasRegistKey,
    bool Stops,
    string? QuitReason);

/// <summary>
/// PP504, under PP340: where a PSN session's registration secrets come from, and the wait that can
/// skip them.
///
/// chiaki_session_init forks on whether there is a holepunch session and the arms are not
/// symmetrical. The local one resolves the host address with getaddrinfo and copies the CALLER's
/// regist_key and morning into the session. The PSN one copies the account id and neither key.
///
/// THAT IS NOT AN OMISSION. A PSN session registers before it does anything else: the block guarded
/// by session-&gt;rudp starts a regist over the just-punched channel, and its callback is what writes
/// morning and regist_key. So the two paths take their secrets from different places - the caller's
/// locally, the console's over PSN - and a port that tidied the fork by copying in both arms would
/// carry stale caller keys that the callback then overwrites.
///
/// THE WAIT HAS THREE OUTCOMES AND TWO ARE HANDLED. Cancelled and failed both set
/// CHIAKI_QUIT_REASON_PSN_REGIST_FAILED and should_stop, so the CHECK_STOP below catches them. The
/// third is the predicate never coming true inside ten seconds: nothing is set, CHECK_STOP passes,
/// and the flow goes on to request a session with regist_key still zeroed.
///
/// WHAT THAT PRODUCES IS NOT SILENCE. The request builder scans the key for its first NUL to find
/// its length, finds one at index zero, and formats an empty hex string - so the console gets a
/// session request carrying no registration key and refuses it. The failure arrives as a refused
/// session request, which is the one cause it is not. Naming the outcome here is what stops a
/// managed flow reproducing it by accident.
/// </summary>
public static class SessionRegistFork
{
    /// <summary>How long the session waits for the registration to finish.</summary>
    public const int RegistWaitMs = 10000;

    /// <summary>The reason the two handled failures set.</summary>
    public const string RegistFailedReason = "CHIAKI_QUIT_REASON_PSN_REGIST_FAILED";

    /// <summary>
    /// The length the request builder gives a key, by scanning to its first zero byte.
    ///
    /// An all-zero key is length zero, which is what makes the timeout arm produce an empty hex
    /// field rather than a malformed one.
    /// </summary>
    public static int KeyLength(ReadOnlySpan<byte> registKey)
    {
        for (var i = 0; i < registKey.Length; i++)
        {
            if (registKey[i] == 0)
                return i;
        }

        return registKey.Length;
    }

    /// <summary>
    /// Runs the fork and, for a PSN session, the wait that follows it.
    /// </summary>
    /// <param name="arm">Which arm.</param>
    /// <param name="callerHasKeys">Whether the caller supplied regist_key and morning.</param>
    /// <param name="wait">
    /// How the registration ended. Ignored for a local session, which never starts one.
    /// </param>
    public static SessionRegistOutcome Run(
        SessionArm arm, bool callerHasKeys = true, RegistWaitOutcome wait = RegistWaitOutcome.Succeeded)
    {
        if (arm == SessionArm.Local)
        {
            // Resolved up front, and the keys are the caller's - nothing later writes them.
            return new SessionRegistOutcome(
                arm, AddressResolved: true, KeysFromCaller: callerHasKeys, KeysFromConsole: false,
                HasRegistKey: callerHasKeys, Stops: false, QuitReason: null);
        }

        // No getaddrinfo: the address arrives from the punch, at the session request.
        return wait switch
        {
            RegistWaitOutcome.Succeeded => new SessionRegistOutcome(
                arm, false, KeysFromCaller: false, KeysFromConsole: true,
                HasRegistKey: true, Stops: false, QuitReason: null),

            RegistWaitOutcome.Canceled or RegistWaitOutcome.Failed => new SessionRegistOutcome(
                arm, false, false, false, HasRegistKey: false, Stops: true,
                QuitReason: RegistFailedReason),

            // The unhandled one. No reason, no stop, and no key.
            _ => new SessionRegistOutcome(
                arm, false, false, false, HasRegistKey: false, Stops: false, QuitReason: null),
        };
    }

    /// <summary>
    /// The outcomes that reach the session request, and whether they carry a key.
    ///
    /// Two do. One of those two has nothing to send, and that is the whole line.
    /// </summary>
    public static bool ReachesTheRequestWithoutAKey(RegistWaitOutcome wait)
    {
        SessionRegistOutcome outcome = Run(SessionArm.Psn, wait: wait);
        return !outcome.Stops && !outcome.HasRegistKey;
    }
}

/// <summary>
/// PP504: the C's own fork and its three arms.
/// </summary>
public static class SessionRegistForkSource
{
    /// <summary>session.c.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(PunchProgressSource.SessionRelativePath);

    /// <summary>chiaki_session_init, where the fork is.</summary>
    public static string? InitBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_session_init");

    /// <summary>The regist callback, which is what writes the keys for a PSN session.</summary>
    public static string? RegistCallbackBody(string source)
        => CFunction.Body(source, "static void regist_cb");

    /// <summary>
    /// Whether only the LOCAL arm still copies the caller's keys.
    ///
    /// Both halves: the PSN arm copies the account id and neither key, the local arm copies both.
    /// A copy appearing in the PSN arm is the tidy-up this exists to catch.
    /// </summary>
    public static bool OnlyTheLocalArmCopiesTheCallersKeys(string initBody)
    {
        ArgumentNullException.ThrowIfNull(initBody);

        string text = initBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        int fork = text.IndexOf("if(session->holepunch_session)", StringComparison.Ordinal);
        if (fork < 0)
            return false;

        int elseArm = text.IndexOf("\n\telse\n", fork, StringComparison.Ordinal);
        if (elseArm < 0)
            return false;

        string psn = text[fork..elseArm];
        string local = text[elseArm..];

        return psn.Contains("session->connect_info.psn_account_id", StringComparison.Ordinal)
            && !psn.Contains("connect_info.regist_key", StringComparison.Ordinal)
            && !psn.Contains("connect_info.morning", StringComparison.Ordinal)
            && local.Contains("getaddrinfo(connect_info->host", StringComparison.Ordinal)
            && local.Contains("session->connect_info.regist_key", StringComparison.Ordinal)
            && local.Contains("session->connect_info.morning", StringComparison.Ordinal);
    }

    /// <summary>Whether the regist callback still writes both keys on success.</summary>
    public static bool TheCallbackWritesBothKeys(string callbackBody)
    {
        ArgumentNullException.ThrowIfNull(callbackBody);

        return callbackBody.Contains(
                "memcpy(session->connect_info.morning, event->registered_host->rp_key",
                StringComparison.Ordinal)
            && callbackBody.Contains(
                "memcpy(session->connect_info.regist_key, event->registered_host->rp_regist_key",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether both handled failures still set the reason AND should_stop.
    ///
    /// Two arms, counted rather than found once: losing either would turn that failure into the
    /// third outcome, which is the one with no reason at all.
    /// </summary>
    public static bool BothFailureArmsStopTheSession(string callbackBody)
    {
        ArgumentNullException.ThrowIfNull(callbackBody);

        string text = callbackBody.Replace("\r\n", "\n", StringComparison.Ordinal);

        return Count(text, $"session->quit_reason = {SessionRegistFork.RegistFailedReason};") == 2
            && text.Contains("CHIAKI_REGIST_EVENT_TYPE_FINISHED_CANCELED", StringComparison.Ordinal)
            && text.Contains("CHIAKI_REGIST_EVENT_TYPE_FINISHED_FAILED", StringComparison.Ordinal);

        static int Count(string haystack, string needle)
        {
            var found = 0;
            for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
                 at >= 0;
                 at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                found++;
            }

            return found;
        }
    }

    /// <summary>
    /// Whether the wait is still bounded at the value this models, with a CHECK_STOP after it.
    ///
    /// The CHECK_STOP is what makes the two handled outcomes handled - and what lets the third
    /// through, since it sets nothing for the check to see.
    /// </summary>
    public static bool TheWaitIsBoundedAndFollowedByCheckStop(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        int wait = text.IndexOf(
            $"chiaki_cond_timedwait_pred(&session->state_cond, &session->state_mutex, {SessionRegistFork.RegistWaitMs}, session_check_state_pred_regist, session);",
            StringComparison.Ordinal);

        if (wait < 0)
            return false;

        int check = text.IndexOf("CHECK_STOP(quit);", wait, StringComparison.Ordinal);

        return check > wait
            && !text[wait..check].Contains("psn_regist_succeeded", StringComparison.Ordinal);
    }
}
