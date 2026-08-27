using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the ctrl thread does with a login-PIN request.</summary>
public enum PinRequestOutcome
{
    /// <summary>No session id yet, so the session is asked to prompt.</summary>
    Prompt,

    /// <summary>
    /// A session id had already arrived, so the session ends instead of prompting.
    /// </summary>
    RefusedTooLate,
}

/// <summary>One request's whole effect: the two flags and the reason, if any.</summary>
/// <param name="Outcome">Which of the two paths ran.</param>
/// <param name="CtrlPinRequested">
/// ctrl's own <c>login_pin_requested</c>. Raised before the decision on both paths, and never
/// lowered by either.
/// </param>
/// <param name="SessionPinRequested">
/// The session's <c>ctrl_login_pin_requested</c> - the flag its wait actually reads. Raised only
/// where the request was in time.
/// </param>
/// <param name="Passes">
/// The reason handed to <c>ctrl_failed</c>, or null where nothing failed. Whether it is RECORDED is
/// a separate question - see <see cref="CtrlLoginPinRequest.ReasonRecorded"/>.
/// </param>
public readonly record struct PinRequestEffect(
    PinRequestOutcome Outcome,
    bool CtrlPinRequested,
    bool SessionPinRequested,
    ChiakiQuitReason? Passes);

/// <summary>
/// PP411, under PP294: the login-PIN request, and the one that arrives too late.
///
/// PP408 modelled the ANSWER - what the console says about a PIN it was given. PP335 has the loop
/// the session thread runs and PP345 the handover into ctrl. This is the REQUEST that starts all
/// three, and it was the piece nobody had written down.
///
/// THE LATE REQUEST ENDS THE SESSION. The handler takes the state mutex and asks whether a session
/// id has already arrived. If one has, the session is failed with
/// <see cref="ChiakiQuitReason.CtrlUnknown"/> and the session's own PIN flag is never raised - the
/// C's comment says "this won't work" and that is the whole of the reasoning on record. A PIN
/// request belongs to connecting; one arriving afterwards is the console asking to start over, and
/// the library refuses rather than putting a PIN dialog over an established stream.
///
/// THE ORDER IS THE PROPERTY WORTH HAVING A NAME FOR. Two flags with the session id test between
/// them. A port that raised the session's flag before testing would prompt on exactly the arrival
/// this refuses, and §PP294 names why a pair table of message-in and message-out would not see it:
/// both orderings send nothing, so only the state tells them apart.
///
/// CTRL'S OWN FLAG IS RAISED FIRST AND UNCONDITIONALLY, and the refusal does not lower it. Nothing
/// reads it afterwards because the session is ending, so it matters only if that refusal ever
/// becomes recoverable - which is the kind of tidying a port does without being asked. Reproduced,
/// and <see cref="PinRequestEffect.CtrlPinRequested"/> is true on both paths so a change would be
/// visible.
///
/// BOTH PATHS SIGNAL THE SESSION, FOR DIFFERENT REASONS. The prompt path signals after raising the
/// session's flag; the refusal signals from inside <c>ctrl_failed</c>, which also sets
/// <c>ctrl_failed</c> unconditionally. So a session waiting on the state condition wakes either
/// way, and what it finds differs. <see cref="LoginPinLoop"/> is the far side of exactly that wait.
///
/// THE REASON IS PASSED, NOT NECESSARILY RECORDED. PP348 guarded <c>ctrl_failed</c> so it writes the
/// quit reason only over NONE. A session already refused for a cause the user could act on keeps
/// that cause, and this handler's CTRL_UNKNOWN is dropped. Modelled as two questions because they
/// are two, and <see cref="SessionTeardownSource.TheGenericCtrlFailureGuards"/> is what holds the
/// guard itself.
///
/// THE SIZE GUARD WARNS AND CARRIES ON, like the login answer's. The request carries nothing the
/// handler reads, so a non-empty payload is logged and ignored rather than refused. PP352's rule is
/// satisfied - the size is looked at - and this is the reporting kind of guard.
/// </summary>
public static class CtrlLoginPinRequest
{
    /// <summary>The message type the console sends this as.</summary>
    public const ushort MessageType = (ushort)CtrlMessage.LoginPinReq;

    /// <summary>The payload size the handler expects. Anything else is warned about.</summary>
    public const int ExpectedPayloadSize = 0;

    /// <summary>Whether a payload of this length draws the warning.</summary>
    public static bool IsWarnedAbout(int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);
        return payloadSize != ExpectedPayloadSize;
    }

    /// <summary>
    /// And whether it stops the handler, which no length does - there is nothing to read.
    /// </summary>
    public static bool IsRefused(int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);
        return false;
    }

    /// <summary>What the handler does with one request.</summary>
    /// <param name="payloadSize">However many bytes arrived. Warned about, never acted on.</param>
    /// <param name="sessionIdReceived">
    /// The session's <c>ctrl_session_id_received</c>, read under the state mutex. This is the whole
    /// of the decision.
    /// </param>
    public static PinRequestEffect Receive(int payloadSize, bool sessionIdReceived)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);

        // Raised first, whatever follows, and not lowered by the refusal below.
        const bool ctrlFlag = true;

        return sessionIdReceived
            ? new PinRequestEffect(
                PinRequestOutcome.RefusedTooLate, ctrlFlag, SessionPinRequested: false,
                ChiakiQuitReason.CtrlUnknown)
            : new PinRequestEffect(
                PinRequestOutcome.Prompt, ctrlFlag, SessionPinRequested: true, Passes: null);
    }

    /// <summary>
    /// Whether the session's state condition is signalled - which it is on both paths.
    ///
    /// A reading rather than a field, because the two paths reach it through different code and a
    /// port could easily signal on one only. A session waiting on that condition would then hang on
    /// the arrival this refuses.
    /// </summary>
    public static bool SignalsTheSession(PinRequestEffect effect)
        => effect.Outcome switch
        {
            // After raising the session's flag.
            PinRequestOutcome.Prompt => true,

            // From inside ctrl_failed, which signals unconditionally.
            PinRequestOutcome.RefusedTooLate => true,

            _ => throw new ArgumentOutOfRangeException(nameof(effect)),
        };

    /// <summary>Whether the session learns the control channel died, which only the refusal says.</summary>
    public static bool ReportsCtrlFailed(PinRequestEffect effect)
        => effect.Outcome == PinRequestOutcome.RefusedTooLate;

    /// <summary>
    /// The reason the session ends up carrying, given whatever it carried already.
    ///
    /// PP348's guard: CTRL_UNKNOWN is written only over NONE, so a session already refused for a
    /// cause the user could act on keeps it.
    /// </summary>
    public static ChiakiQuitReason ReasonRecorded(PinRequestEffect effect, ChiakiQuitReason existing)
        => effect.Passes is { } passed && existing == ChiakiQuitReason.None ? passed : existing;
}

/// <summary>PP411: the handler's rules, still stated the same way in the core.</summary>
public static class CtrlLoginPinRequestSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The handler's body, or null where the signature has moved.</summary>
    public static string? Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // PP359: the whole signature, because a prefix that stops at the name matches a longer one.
        return CFunction.Body(
            core,
            "static void ctrl_message_received_login_pin_req"
                + "(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)");
    }

    /// <summary>
    /// Whether the session id test still stands BETWEEN the two flags.
    ///
    /// This is the ordering assertion. Three marks: ctrl's flag, the test, then the session's flag -
    /// and the third must come after the second or the refusal prompts anyway.
    /// </summary>
    public static bool TheSessionIdTestStillStandsBetweenTheFlags(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int ctrlFlag = CCall.Mark(code, "ctrl->login_pin_requested = true;");
        int test = CCall.Mark(code, "if(ctrl->session->ctrl_session_id_received)");
        int sessionFlag = CCall.Mark(code, "ctrl->session->ctrl_login_pin_requested = true;");

        return ctrlFlag >= 0 && test > ctrlFlag && sessionFlag > test;
    }

    /// <summary>
    /// Whether the late request still ends the session with the reason this port names.
    ///
    /// The unlock before the failure is part of it: <c>ctrl_failed</c> takes the same mutex, so a
    /// path reaching it still holding one would deadlock rather than refuse.
    /// </summary>
    public static bool TheLateRequestStillEndsTheSession(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int test = CCall.Mark(code, "if(ctrl->session->ctrl_session_id_received)");
        if (test < 0)
            return false;

        int unlock = CCall.Mark(code, "chiaki_mutex_unlock(&ctrl->session->state_mutex)", test);
        int failed = CCall.Mark(code, "ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN)", test);

        return unlock > test && failed > unlock;
    }

    /// <summary>
    /// And whether the refusal still returns rather than falling through to the prompt.
    ///
    /// Without the return, the session's flag would be raised after the failure and a PIN prompt
    /// would go up over a session already ending.
    /// </summary>
    public static bool TheRefusalStillReturns(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int failed = CCall.Mark(code, "ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN)");
        int returns = CCall.Mark(code, "return;", Math.Max(failed, 0));
        int sessionFlag = CCall.Mark(code, "ctrl->session->ctrl_login_pin_requested = true;");

        return failed >= 0 && returns > failed && sessionFlag > returns;
    }

    /// <summary>Whether ctrl's own flag is still raised before the mutex is even taken.</summary>
    public static bool CtrlsFlagIsStillRaisedFirst(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int flag = CCall.Mark(code, "ctrl->login_pin_requested = true;");
        int taken = CCall.Mark(code, "chiaki_mutex_lock(&ctrl->session->state_mutex)");

        return flag >= 0 && taken > flag;
    }

    /// <summary>
    /// Whether the size guard still warns without refusing - no early return on any length.
    ///
    /// PP352's rule is that the size is looked at. This is the other half: that looking at it does
    /// not stop the handler, because the request carries nothing worth reading.
    /// </summary>
    public static bool TheGuardStillOnlyWarns(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int guard = CCall.Mark(code, "if(payload_size != 0)");
        int flag = CCall.Mark(code, "ctrl->login_pin_requested = true;");

        // The guard exists, the handler's first real act is reached past it, and nothing returns in
        // between - which is the whole of "warns without refusing", read without caring whether the
        // warning is braced.
        int returns = CCall.Mark(code, "return;");

        return guard >= 0 && flag > guard && (returns < 0 || returns > flag);
    }
}
