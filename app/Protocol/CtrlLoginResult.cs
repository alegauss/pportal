using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the console said about the PIN it was given.</summary>
public enum CtrlLoginState : byte
{
    /// <summary>It was right, and the connect carries on.</summary>
    Success = 0x0,

    /// <summary>It was wrong, and the console will take another.</summary>
    PinIncorrect = 0x1,
}

/// <summary>What the ctrl thread does with a login message.</summary>
public enum LoginOutcome
{
    /// <summary>The payload was empty, so there was no state to read.</summary>
    Ignored,

    /// <summary>Accepted: ctrl stops considering a PIN outstanding.</summary>
    Accepted,

    /// <summary>Refused while a PIN was outstanding: the session is asked to prompt again.</summary>
    PromptAgain,

    /// <summary>Refused with no PIN outstanding. Logged, and nothing else.</summary>
    Unsolicited,

    /// <summary>A state neither value names. Logged, and nothing else.</summary>
    Unknown,
}

/// <summary>
/// PP408, under PP294: what the console answers a PIN with.
///
/// PP335 ported the login-PIN loop as the session thread runs it and PP345 ported the handover of a
/// PIN into ctrl. <c>ctrl_message_received_login</c> is what sits between them, and it was the piece
/// nobody had written down.
///
/// THE SIZE GUARD REPORTS RATHER THAN REFUSES. It warns when the payload is not exactly one byte,
/// and returns only when it is under one - so a two-byte answer is accepted and its first byte used.
/// PP352's rule is satisfied: the size is looked at before the byte is read. This is the other kind
/// of guard, the one that says something is wrong and carries on anyway.
///
/// PIN INCORRECT DOES NOTHING UNSOLICITED. The handler tests ctrl's own <c>login_pin_requested</c>
/// first and warns where it is false. That is the property worth having a name for: it is what
/// stands between a stray control message and a PIN dialog appearing over somebody's stream.
///
/// SUCCESS CLEARS CTRL'S FLAG AND NOT THE SESSION'S, which reads as an omission and is not one.
/// session.c consumes <c>ctrl_login_pin_requested</c> in the while that waits on it, setting it
/// false as it takes it. Checked rather than assumed - the two flags have nearly the same name and
/// only one of them is ctrl's to clear. See <see cref="LoginPinLoop"/> for the far side.
/// </summary>
public static class CtrlLoginResult
{
    /// <summary>The message type the console sends this as.</summary>
    public const ushort MessageType = (ushort)CtrlMessage.Login;

    /// <summary>The payload size the handler expects. Anything else is warned about.</summary>
    public const int ExpectedPayloadSize = 1;

    /// <summary>
    /// Whether a payload of this length draws the warning - which is not the same as being refused.
    /// </summary>
    public static bool IsWarnedAbout(int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);
        return payloadSize != ExpectedPayloadSize;
    }

    /// <summary>And whether it is short enough to stop the handler, which only empty is.</summary>
    public static bool IsRefused(int payloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadSize);
        return payloadSize < ExpectedPayloadSize;
    }

    /// <summary>
    /// What the handler does with one login message.
    /// </summary>
    /// <param name="payload">The message payload, whose first byte is the state.</param>
    /// <param name="pinOutstanding">
    /// ctrl's own <c>login_pin_requested</c> - whether this ctrl asked for a PIN and has not been
    /// answered. NOT the session's flag of nearly the same name.
    /// </param>
    public static LoginOutcome Receive(ReadOnlySpan<byte> payload, bool pinOutstanding)
    {
        if (IsRefused(payload.Length))
            return LoginOutcome.Ignored;

        // Past the guard, only the first byte is read, however many arrived.
        return (CtrlLoginState)payload[0] switch
        {
            CtrlLoginState.Success => LoginOutcome.Accepted,
            CtrlLoginState.PinIncorrect => pinOutstanding
                ? LoginOutcome.PromptAgain
                : LoginOutcome.Unsolicited,
            _ => LoginOutcome.Unknown,
        };
    }

    /// <summary>Whether this outcome leaves ctrl considering a PIN outstanding.</summary>
    /// <remarks>
    /// Only success clears it, and only a request sets it. A refusal leaves it exactly as it was,
    /// which is what lets the console refuse twice and be prompted twice.
    /// </remarks>
    public static bool StillOutstanding(LoginOutcome outcome, bool wasOutstanding)
        => outcome != LoginOutcome.Accepted && wasOutstanding;

    /// <summary>And whether the session is told to prompt.</summary>
    public static bool AsksTheSessionToPrompt(LoginOutcome outcome)
        => outcome == LoginOutcome.PromptAgain;
}

/// <summary>PP408: the handler's rules, still stated the same way in the core.</summary>
public static class CtrlLoginResultSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The handler's body, or null where the signature has moved.</summary>
    /// <remarks>
    /// PP359: the whole signature, because a prefix that stops at the name matches a longer one -
    /// which is how a body for a different function got read once.
    /// </remarks>
    public static string? Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return CFunction.Body(
            core,
            "static void ctrl_message_received_login(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)");
    }

    /// <summary>Whether the two states still carry the values this port gives them.</summary>
    public static bool TheStatesAreStillThese(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string code = CCall.Code(core);

        return code.Contains(
                $"CTRL_LOGIN_STATE_SUCCESS = 0x{(byte)CtrlLoginState.Success:x}", StringComparison.Ordinal)
            && code.Contains(
                $"CTRL_LOGIN_STATE_PIN_INCORRECT = 0x{(byte)CtrlLoginState.PinIncorrect:x}",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the size guard still warns without refusing - the two tests, in that order.
    /// </summary>
    public static bool TheGuardStillReportsRatherThanRefuses(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int warned = CCall.Mark(code, "if(payload_size != 1)");
        int refused = CCall.Mark(code, "if(payload_size < 1)");

        return warned >= 0 && refused > warned;
    }

    /// <summary>And whether an unsolicited refusal is still tested for before it acts.</summary>
    public static bool AnUnsolicitedRefusalStillDoesNothing(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int state = CCall.Mark(code, "case CTRL_LOGIN_STATE_PIN_INCORRECT:");
        int guard = CCall.Mark(code, "if(ctrl->login_pin_requested)");
        int raise = CCall.Mark(code, "ctrl->session->ctrl_login_pin_requested = true;");

        return state >= 0 && guard > state && raise > guard;
    }

    /// <summary>Whether success still clears ctrl's flag and leaves the session's alone.</summary>
    public static bool SuccessStillClearsOnlyCtrlsFlag(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string? body = Body(core);
        if (body is null)
            return false;

        string code = CCall.Code(body);
        int success = CCall.Mark(code, "case CTRL_LOGIN_STATE_SUCCESS:");
        int clear = CCall.Mark(code, "ctrl->login_pin_requested = false;");

        return success >= 0
            && clear > success
            && CCall.Mark(code, "ctrl->session->ctrl_login_pin_requested = false;") < 0;
    }

    /// <summary>And whether the session is still the one that clears its own.</summary>
    public static bool TheSessionStillConsumesItsOwnFlag(string sessionCore)
    {
        ArgumentNullException.ThrowIfNull(sessionCore);

        string code = CCall.Code(sessionCore);
        int wait = CCall.Mark(code, "while(session->ctrl_login_pin_requested)");
        int clear = CCall.Mark(code, "session->ctrl_login_pin_requested = false;", Math.Max(wait, 0));

        return wait >= 0 && clear > wait;
    }
}
