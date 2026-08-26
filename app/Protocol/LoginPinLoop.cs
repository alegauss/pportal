namespace ChiakiNg.Protocol;

/// <summary>What the login-PIN loop does next.</summary>
public enum PinStep
{
    /// <summary>Ask the user for a PIN, and wait however long they take.</summary>
    Prompt,

    /// <summary>Hand the PIN the user typed to ctrl, then wait for a session id or another prompt.</summary>
    Forward,

    /// <summary>The console accepted it: the connect sequence carries on.</summary>
    Done,

    /// <summary>The session was asked to stop.</summary>
    Stopped,

    /// <summary>Ctrl died, which ends the session whichever wait it happened in.</summary>
    CtrlFailed,
}

/// <summary>One turn of the loop.</summary>
/// <param name="Step">What happens next.</param>
/// <param name="PinIncorrect">
/// What the prompt tells the user, where <see cref="Step"/> is <see cref="PinStep.Prompt"/>.
/// </param>
public readonly record struct PinTurn(PinStep Step, bool PinIncorrect = false);

/// <summary>
/// PP335, continuing PP293: the login-PIN loop, which asks again for as long as the console keeps
/// asking, and lies about the first refusal being one.
///
/// PP293's earlier part ported the five wait PREDICATES. This is the loop wrapped around two of
/// them: ctrl asks for a PIN, the user is prompted, the PIN goes to ctrl, and the thread waits for
/// either a session id or another request - which is the console saying it was wrong.
///
/// pin_incorrect IS SET BEFORE THE WAIT, NOT AFTER A REFUSAL. It starts false, the prompt is sent,
/// and then it is assigned true unconditionally - so it describes the NEXT prompt rather than this
/// one.
///
/// PP357 CORRECTS WHY. The first version of this note said no ctrl signal ever reports a rejected
/// PIN. One does: a LOGIN message carrying CTRL_LOGIN_STATE_PIN_INCORRECT, which
/// ctrl_message_received_login answers by re-raising ctrl_login_pin_requested - the SAME flag it
/// used to ask the first time. So the information exists and is flattened at the seam: the ctrl
/// thread knows the PIN was wrong and the session thread receives only "a PIN is wanted".
///
/// Which leaves the behaviour unchanged and the reason different. This loop still cannot tell a
/// first request from a refusal, so the flag still has to be set in advance - but a port looking
/// for the signal would find it, one layer down, and could carry it across if anybody wanted the
/// distinction.
///
/// THE PROMPT WAIT IS UNBOUNDED. Every other wait in the connect sequence has a timeout;
/// session.c passes UINT64_MAX here, because what it is waiting for is a person typing. Reproduced
/// rather than capped: a timeout would end sessions of anyone who had to go and find the console.
///
/// THE TWO ENDINGS ARE CHECKED IN THE SAME ORDER AS EVERYWHERE ELSE. Stop, then ctrl failure, then
/// the thing waited for - and ctrl failure is checked separately after BOTH waits in the C, which
/// is why a failure during PIN entry ends the session rather than forwarding a PIN to a dead ctrl.
/// </summary>
public static class LoginPinLoop
{
    /// <summary>
    /// What the loop does, given where it is and what the session looks like.
    /// </summary>
    /// <param name="state">The shared state the waits read.</param>
    /// <param name="prompted">
    /// Whether a prompt has already gone out this connect. This is pin_incorrect: it is what the C
    /// carries across iterations, and the only thing the loop remembers.
    /// </param>
    /// <param name="waiting">Which of the loop's two waits the thread is in.</param>
    public static PinTurn Next(SessionState state, bool prompted, PinWait waiting)
    {
        // Both endings first, in the order every call site in session.c checks them. A session that
        // was asked to stop AND has a PIN typed must stop.
        if (state.ShouldStop)
            return new PinTurn(PinStep.Stopped);

        if (state.CtrlFailed)
            return new PinTurn(PinStep.CtrlFailed);

        return waiting switch
        {
            // Waiting for the user. Their PIN goes to ctrl; until then, nothing moves.
            PinWait.ForThePerson => state.LoginPinEntered
                ? new PinTurn(PinStep.Forward)
                : new PinTurn(PinStep.Prompt, prompted),

            // Waiting for ctrl's answer. A session id ends the loop; another request means the PIN
            // was wrong, and the next prompt says so.
            _ when state.CtrlSessionIdReceived => new PinTurn(PinStep.Done),
            _ when state.CtrlLoginPinRequested => new PinTurn(PinStep.Prompt, prompted),

            // Neither yet: the wait has not ended.
            _ => new PinTurn(PinStep.Prompt, prompted),
        };
    }

    /// <summary>
    /// Whether the wait the loop is about to enter has a timeout.
    ///
    /// Only one of the two does. The prompt wait is UINT64_MAX because a person is typing; the wait
    /// for ctrl's answer uses the same bound the initial ctrl start does.
    /// </summary>
    public static bool IsBounded(PinWait waiting) => waiting != PinWait.ForThePerson;

    /// <summary>
    /// What a prompt says, which is a function of how many have gone out and nothing else.
    ///
    /// The first is "the console is asking"; every one after it is "that was wrong". No refusal is
    /// ever reported to this loop - a second request IS the refusal.
    /// </summary>
    public static bool SaysTheLastOneWasWrong(int promptsSoFar) => promptsSoFar > 0;
}

/// <summary>Which of the loop's two waits the thread is in.</summary>
public enum PinWait
{
    /// <summary>For the user to type a PIN. Unbounded.</summary>
    ForThePerson,

    /// <summary>For ctrl to answer with a session id or another request.</summary>
    ForTheConsole,
}

/// <summary>
/// PP335: the loop held against session_thread_func, because none of it is in the recording.
///
/// PP297's capture is of a console that asked for no PIN, so every line of this is asserted against
/// session.c and against nothing else.
/// </summary>
public static class LoginPinSource
{
    /// <summary>Where the loop lives.</summary>
    public const string RelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => ChiakiNg.Session.SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether pin_incorrect is still set unconditionally after the prompt rather than on a refusal.
    /// </summary>
    public static bool TheFlagIsStillSetBeforeTheWait(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int loop = core.IndexOf("while(session->ctrl_login_pin_requested)", StringComparison.Ordinal);
        if (loop < 0)
            return false;

        int send = core.IndexOf("chiaki_session_send_event(session, &event);", loop, StringComparison.Ordinal);
        int assign = core.IndexOf("pin_incorrect = true;", loop, StringComparison.Ordinal);
        int wait = core.IndexOf("session_check_state_pred_pin", loop, StringComparison.Ordinal);

        return send > 0 && assign > send && wait > assign;
    }

    /// <summary>Whether the wait for the person is still unbounded.</summary>
    public static bool ThePromptWaitIsStillUnbounded(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("UINT64_MAX, session_check_state_pred_pin", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether ctrl failure is still checked separately inside the loop, after the PIN wait.
    ///
    /// Without it a PIN typed while ctrl was dying is forwarded to a ctrl that is gone.
    /// </summary>
    public static bool CtrlFailureIsStillCheckedInsideTheLoop(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("Ctrl has failed while waiting for PIN entry", StringComparison.Ordinal);
    }
}
