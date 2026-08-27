using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP383: no send in the control channel has its answer discarded, and the feature burst reports.
///
/// ctrl_enable_features sent seven messages, read none of them, and returned void - so the SESSION_ID
/// arm that calls it could not learn anything either. PP342 modelled that burst and PP297's capture
/// holds three of the seven; what nothing modelled was what happens when one does not go.
///
/// THE COUNTER IS WHY THIS IS NOT ABOUT FEATURES. ctrl_message_send spends crypt_counter_local at
/// ENCRYPT time, before a byte reaches the socket, so a send that fails has already consumed a value
/// the console never saw. From there the two sides disagree and every later ctrl message decrypts
/// against the wrong counter. A DualSense enable that did not go is not a controller without
/// haptics; it is a control channel that stops working shortly afterwards, for a reason nothing
/// logged.
///
/// SO THE BURST STOPS AT THE FIRST FAILURE. Sending the rest would spend more counter values into a
/// gap that is already open, and there is nothing to salvage: the feature this call was for is the
/// least of what has gone.
///
/// AND BOTH CALLERS END THE CHANNEL. The handler is void and reaches ctrl_failed; the session
/// thread's copy has a label for it already. PP370's rule said the result must be READ rather than
/// that failing must end anything - here it does end something, because a drifted counter has no
/// other answer.
/// </summary>
public static class CtrlSendResults
{
    /// <summary>Where the sends live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The sends in ctrl.c that answer something.
    ///
    /// ctrl_message_send is the one every other goes through, so it is the one a discard is likely
    /// to be. The wrappers are named too, for the reason PP379's list is: a rule over one name
    /// covers one shape of mistake.
    /// </summary>
    public static IReadOnlyList<string> SendsThatAnswer { get; } =
    [
        "ctrl_message_send",
        "ctrl_message_toggle_microphone",
        "ctrl_message_connect_microphone",
        "ctrl_message_go_home",
        "ctrl_message_set_fallback_session_id",
        "ctrl_enable_features",
    ];

    /// <summary>
    /// Every call to one of them whose result goes nowhere.
    ///
    /// Through PP370's reader, which PP379 lifted out for exactly this: the shape of a discard is a
    /// fact about C and only the list is a fact about the file.
    /// </summary>
    public static IReadOnlyList<string> DiscardedResults(string source)
        => StreamSendResults.DiscardedCalls(source, SendsThatAnswer);

    /// <summary>
    /// How many discards ctrl.c still holds, which is none.
    ///
    /// PP383 fixed the burst and shipped this as a ceiling of seven, because stating the rule over
    /// the file found seven more and each was a different decision. PP385 answered all seven, so
    /// the ratchet is at zero and ctrl.c joins streamconnection.c and senkusha.c in asserting it
    /// flatly. Lowered in the commit that earned it, which is the rule the assertion ratchet states
    /// for shipped tasks and the same one applies here.
    /// </summary>
    public const int DiscardCeiling = 0;

    /// <summary>
    /// PP385: whether the drain still LEAVES on a failed send rather than draining on.
    ///
    /// The node is unlinked and freed either way, so there is nothing to retry - and the counter
    /// has moved, so every message still queued would spend another value into the same gap. The
    /// break is what separates this from a log.
    /// </summary>
    public static bool TheDrainLeavesOnAFailedSend(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int drain = source.IndexOf("while(ctrl->msg_queue)", StringComparison.Ordinal);
        if (drain < 0)
            return false;

        int read = source.IndexOf("drain_err = ctrl_message_send(", drain, StringComparison.Ordinal);
        int tested = source.IndexOf("if(drain_err != CHIAKI_ERR_SUCCESS)", read < 0 ? drain : read, StringComparison.Ordinal);
        int failed = source.IndexOf("ctrl_failed(ctrl,", tested < 0 ? drain : tested, StringComparison.Ordinal);
        int left = source.IndexOf("break;", failed < 0 ? drain : failed, StringComparison.Ordinal);

        return read > drain && tested > read && failed > tested && left > failed;
    }

    /// <summary>
    /// PP416: and whether leaving the drain now drops what is still queued.
    ///
    /// THE BREAK ABOVE WAS NOT ENOUGH, which is why this is a second check rather than a wider
    /// version of the first. <see cref="TheDrainLeavesOnAFailedSend"/> asserts the break, and the
    /// break exits the INNER loop only: the outer loop's test on should_stop, msg_queue and
    /// login_pin_entered was then true because the queue was not empty, so it took the cancelled
    /// branch and re-entered the drain. Every message PP385 meant to hold back went out anyway, one
    /// per outer iteration, and how many depended on when the session thread got round to stopping
    /// ctrl - a count that changed between runs.
    ///
    /// Anchored on the failure test rather than on the drain's own while, because there are now two
    /// of those inside the branch and the second one is the subject.
    /// </summary>
    public static bool TheDrainDropsWhatIsStillQueued(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int tested = source.IndexOf("if(drain_err != CHIAKI_ERR_SUCCESS)", StringComparison.Ordinal);
        if (tested < 0)
            return false;

        // Comments stripped: the note explaining this fix quotes the outer loop's test, and PP400's
        // rule is that prose must not satisfy a search about code.
        string branch = CCall.Compact(CCall.Code(source[tested..]));

        int drop = branch.IndexOf("while(ctrl->msg_queue)", StringComparison.Ordinal);
        if (drop < 0)
            return false;

        int freed = branch.IndexOf(
            "ctrl_message_queue_free(rest)", drop, StringComparison.Ordinal);
        if (freed < 0)
            return false;

        int left = branch.IndexOf("break;", freed, StringComparison.Ordinal);

        return left > freed;
    }

    /// <summary>
    /// Whether the queued message's type is copied before the node is freed.
    ///
    /// The log wants it and ctrl_message_queue_free ends the node it lives in, so reading it after
    /// is a use-after-free - which is what the first version of this fix did.
    /// </summary>
    public static bool TheDrainCopiesTheTypeBeforeTheFree(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source); // PP388

        int copied = CCall.Mark(compact, "uint16_t drain_type = msg->type;");
        if (copied < 0)
            return false;

        int freed = CCall.At(compact, "ctrl_message_queue_free(msg)", copied);
        int logged = CCall.Mark(compact, "(unsigned int)drain_type", freed < 0 ? copied : freed);

        return freed > copied && logged > freed;
    }

    /// <summary>
    /// Whether the fallback session id is reported without ending anything.
    ///
    /// It sends nothing, so no counter moves - the failure is that the session has no id, and the
    /// session thread's own check already ends on that. A log is the whole of what was owed, and a
    /// ctrl_failed here would be this port ending sessions the C carries on with.
    /// </summary>
    public static bool TheFallbackIsReportedAndNotFatal(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int guard = source.IndexOf("#define CTRL_FALLBACK_SESSION_ID(", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int end = source.IndexOf("} while(0)", guard, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string macro = source[guard..end];

        return macro.Contains("fallback_err != CHIAKI_ERR_SUCCESS", StringComparison.Ordinal)
            && macro.Contains("CHIAKI_LOGE", StringComparison.Ordinal)
            && !macro.Contains("ctrl_failed(", StringComparison.Ordinal);
    }

    /// <summary>How many of the session-id ladder's rungs go through that guard. Four.</summary>
    public static int FallbackCallsThroughTheGuard(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var count = 0;
        const string Call = "CTRL_FALLBACK_SESSION_ID(ctrl);";

        for (int at = source.IndexOf(Call, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(Call, at + Call.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The seven messages the burst sends, in the order PP342 established.
    ///
    /// Named so the rule below is about all of them: a burst that grew an eighth unchecked send
    /// would satisfy a count and not this.
    /// </summary>
    public static IReadOnlyList<string> Burst { get; } =
    [
        "CTRL_MESSAGE_TYPE_ENABLE_DUALSENSE_FEATURES",
        "0x11",
        "CTRL_MESSAGE_TYPE_KEYBOARD_ENABLE",
        "CTRL_MESSAGE_TYPE_KEYBOARD_ENABLE_TOGGLE",
        "ctrl_message_toggle_microphone",
        "ctrl_message_toggle_microphone",
        "CTRL_MESSAGE_TYPE_DISPLAY_DEVICES",
    ];

    /// <summary>
    /// Whether the burst can report at all, read off its declaration.
    ///
    /// The header rather than the body, for PP345's reason: a body that returns a code from a
    /// function declared void does not compile, and one that does not is what a caller cannot see.
    /// </summary>
    public static bool TheBurstCanReportAFailure(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Contains("ChiakiErrorCode ctrl_enable_features(", StringComparison.Ordinal)
            && !header.Contains("void ctrl_enable_features(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether every send inside the burst is read, counted rather than located.
    ///
    /// What is required is that the body holds no bare send at all: each goes through the guard
    /// that tests and returns. A count is the right shape because the burst is a list that grows.
    /// </summary>
    public static int UncheckedSendsInTheBurst(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "ChiakiErrorCode ctrl_enable_features(");
        if (body is null)
            return -1;

        return StreamSendResults.DiscardedCalls(body, SendsThatAnswer).Count;
    }

    /// <summary>
    /// Whether the burst still stops at the first failure rather than sending on.
    ///
    /// The guard has to RETURN. One that only logged would satisfy "the result is read" and would
    /// still spend the remaining counter values into a gap the console does not know about.
    /// </summary>
    public static bool TheBurstStopsAtTheFirstFailure(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int guard = source.IndexOf("#define CTRL_FEATURE_SEND(", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int end = source.IndexOf("} while(0)", guard, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string macro = source[guard..end];

        return macro.Contains("feature_err != CHIAKI_ERR_SUCCESS", StringComparison.Ordinal)
            && macro.Contains("CHIAKI_LOGE", StringComparison.Ordinal)
            && macro.Contains("return feature_err;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether both callers end the channel on a failed burst.
    ///
    /// Two files and two mechanisms - ctrl_failed in the handler, the ctrl_failed LABEL in the
    /// session thread - so this takes each source and asks for the one that belongs to it.
    /// </summary>
    public static bool TheHandlerEndsTheChannel(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        int arm = ctrlSource.IndexOf("case CTRL_MESSAGE_TYPE_SESSION_ID:", StringComparison.Ordinal);
        if (arm < 0)
            return false;

        int call = ctrlSource.IndexOf(
            "if(ctrl_enable_features(ctrl) != CHIAKI_ERR_SUCCESS)", arm, StringComparison.Ordinal);
        int failed = ctrlSource.IndexOf("ctrl_failed(ctrl,", call < 0 ? arm : call, StringComparison.Ordinal);
        int next = ctrlSource.IndexOf("case CTRL_MESSAGE_TYPE_HEARTBEAT_REQ:", arm, StringComparison.Ordinal);

        return call > arm && failed > call && (next < 0 || failed < next);
    }

    /// <summary>The same for the session thread's own call.</summary>
    public static bool TheSessionThreadEndsTheChannel(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        int call = sessionSource.IndexOf(
            "err = ctrl_enable_features(&session->ctrl);", StringComparison.Ordinal);
        if (call < 0)
            return false;

        int tested = sessionSource.IndexOf(
            "if(err != CHIAKI_ERR_SUCCESS)", call, StringComparison.Ordinal);
        int jump = sessionSource.IndexOf("goto ctrl_failed;", tested < 0 ? call : tested, StringComparison.Ordinal);

        return tested > call && jump > tested;
    }
}
