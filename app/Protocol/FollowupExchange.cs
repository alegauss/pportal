namespace ChiakiNg.Protocol;

/// <summary>What one turn of the followup loop does.</summary>
public enum FollowupStep
{
    /// <summary>Answer the console's request and wait for the next.</summary>
    Answer,

    /// <summary>An extra response arrived; ignore it and wait again.</summary>
    Ignore,

    /// <summary>
    /// The receive failed. Log and wait again - which is the step with no exit behind it.
    /// </summary>
    Retry,

    /// <summary>Nothing came, and something already had. That is the ordinary ending.</summary>
    Done,

    /// <summary>Nothing came and nothing ever had.</summary>
    TimedOut,

    /// <summary>End the punch.</summary>
    Fatal,
}

/// <summary>
/// PP256: the last thing a successful punch does - keep answering until the console stops asking.
///
/// A RECEIVE THAT FAILS USED TO SPIN, AND PP457 BOUNDED IT. There were four ways out of this loop: a
/// timeout, a select error, a packet of the wrong size, and a message of the wrong type. A failed
/// receive was none of them - it logged and continued, back to a wait that reports the socket
/// readable, back to a receive that fails again. The socket is connected before this call, so on
/// Windows an ICMP rejection arrives as a receive error on a socket select calls readable: no
/// progress, no timeout, one log line per turn.
///
/// <see cref="Leaves"/> still answers which STEPS end the loop and <see cref="FollowupStep.Retry"/> is
/// still not one of them, because the bound is not a step - it is a count above the loop, so the step
/// function is unchanged and a fifth way out exists that no step names. That is the shape of the fix
/// and the reason <see cref="APersistentFailureEnds"/> still answers false.
///
/// THIS AND <see cref="PunchExchange"/> MODEL THE SAME FUNCTION. PP256 and PP238 each ported
/// receive_request_send_response_ps with its own vocabulary and its own source predicates, which is
/// the duplication PP454 found one level down in the packet's offsets. Filed rather than merged here:
/// see the backlog.
///
/// THE TIMEOUT IS THE ORDINARY ENDING. A request answered loops round; only silence ends it. Timing
/// out having heard something is success, and having heard nothing is a timeout - the value PP249
/// measured the caller forgiving when it had already answered a request of its own. The two halves
/// fit: this reports the silence, the caller decides it does not matter.
///
/// AND ONE LINE NAMES NOTHING AT ALL. Three of the four say check_candidates, which PP238 already
/// ruled defensible - they are that operation's, and this function is its helper, which is why it
/// sits in <see cref="MisnamedLogs.NamesTheOperationNotTheFunction"/> rather than beside the
/// genuinely misnamed. The fourth belongs to neither list: it names no operation and no function,
/// so a reader meeting it has only the text to go on. See <see cref="TheLineThatNamesNothing"/>.
/// </summary>
public static class FollowupExchange
{
    /// <summary>How long one wait is given, in seconds.</summary>
    public const int TimeoutSeconds = 1;

    /// <summary>The size a request has to be, which is the probe's.</summary>
    public const int RequestLength = PunchProbe.Length;

    /// <summary>
    /// What one turn does.
    /// </summary>
    /// <param name="readable">Whether the wait said there was something to read.</param>
    /// <param name="received">Whether a request has already been answered this call.</param>
    /// <param name="receiveFailed">Whether the receive itself failed.</param>
    /// <param name="length">How many bytes arrived.</param>
    /// <param name="messageType">The word at the front.</param>
    public static FollowupStep Next(
        bool readable, bool received, bool receiveFailed, int length, uint messageType)
    {
        if (!readable)
            return received ? FollowupStep.Done : FollowupStep.TimedOut;

        // The one outcome with no exit behind it.
        if (receiveFailed)
            return FollowupStep.Retry;

        if (length != RequestLength)
            return FollowupStep.Fatal;

        if (messageType == PunchResponse.ResponseType)
            return FollowupStep.Ignore;

        return messageType == PunchProbe.RequestType ? FollowupStep.Answer : FollowupStep.Fatal;
    }

    /// <summary>Whether a step ends the loop.</summary>
    public static bool Leaves(FollowupStep step)
        => step is FollowupStep.Done or FollowupStep.TimedOut or FollowupStep.Fatal;

    /// <summary>Every step that goes round again.</summary>
    public static IReadOnlyList<FollowupStep> Continues { get; } =
        [.. Enum.GetValues<FollowupStep>().Where(s => !Leaves(s))];

    /// <summary>
    /// Whether a condition that persists can end the loop - which for a failing receive it cannot.
    /// </summary>
    public static bool APersistentFailureEnds(FollowupStep step) => Leaves(step);

    /// <summary>What the caller is told for each ending.</summary>
    public static string CodeFor(FollowupStep step) => step switch
    {
        FollowupStep.Done => "CHIAKI_ERR_SUCCESS",
        FollowupStep.TimedOut => "CHIAKI_ERR_TIMEOUT",
        FollowupStep.Fatal => "CHIAKI_ERR_NETWORK or CHIAKI_ERR_UNKNOWN",
        _ => "",
    };

    /// <summary>
    /// Whether the caller forgives the ending, which PP249 measured - the timeout is forgiven when
    /// the caller itself had already answered a request.
    /// </summary>
    public static bool CallerForgives(FollowupStep step, bool callerAlreadyAnswered)
        => step == FollowupStep.TimedOut && callerAlreadyAnswered;
}

/// <summary>
/// PP256: the loop where the core writes it.
/// </summary>
public static class FollowupExchangeSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>Whether the loop still has no condition of its own.</summary>
    public static bool TheLoopIsStillUnconditional(string core)
        => Body(core).Contains("    while (true)\n", StringComparison.Ordinal);

    /// <summary>
    /// Whether a failed receive still continues, AND whether the loop now bounds how many times it
    /// may.
    ///
    /// PP256 asserted only the first half, and read it as "the spin is present". PP457 bounded the
    /// spin at the top of the loop without touching this block, so the first half stayed true and
    /// said nothing - a predicate that outlived what it described. Both halves are here now: the
    /// block still declines to leave, and something above it counts.
    /// </summary>
    public static bool AFailedReceiveContinuesUnderABound(string core)
    {
        string body = Body(core);

        int fails = body.IndexOf(
            "check_candidates: Receiving response from %s:%d failed with error: ", StringComparison.Ordinal);
        if (fails < 0)
            return false;

        int closes = body.IndexOf("\n        }", fails, StringComparison.Ordinal);
        if (closes < 0)
            return false;

        bool continuesWithoutLeaving = body[fails..closes].Contains("continue;", StringComparison.Ordinal)
            && !body[fails..closes].Contains("return", StringComparison.Ordinal);

        // And the discard it counts is what the bound above the loop reads.
        return continuesWithoutLeaving
            && body[fails..closes].Contains("discarded++;", StringComparison.Ordinal)
            && body.Contains("if(discarded > MAX_CONSECUTIVE_DISCARDS)", StringComparison.Ordinal);
    }

    /// <summary>And whether the wrong-size packet beside it still ends the punch.</summary>
    public static bool TheWrongSizeStillEndsIt(string core)
    {
        string body = Body(core);

        int wrong = body.IndexOf(
            "check_candidates: Received request of unexpected size", StringComparison.Ordinal);
        if (wrong < 0)
            return false;

        int closes = body.IndexOf("\n        }", wrong, StringComparison.Ordinal);
        return closes > wrong
            && body[wrong..closes].Contains("return CHIAKI_ERR_NETWORK;", StringComparison.Ordinal);
    }

    /// <summary>Whether a timeout still means success once something has been heard.</summary>
    public static bool TheTimeoutIsStillSuccessAfterAnything(string core)
        => Body(core).Contains(
            """
            if(err == CHIAKI_ERR_TIMEOUT && received)
                        return CHIAKI_ERR_SUCCESS;
            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    /// <summary>
    /// Whether four lines still name the OPERATION and one still names nothing.
    ///
    /// The four are defensible - PP238 settled that - so what this asserts is the split, not a count
    /// of wrongs. The fifth is the one with nothing on it.
    ///
    /// It was three of four until PP457 added the discard-limit line, which names the operation like
    /// its neighbours. The numbers move with the file rather than the check being loosened, because
    /// the split is the thing worth holding: a line appearing with no name at all, or with its own
    /// function's, is a change somebody decided and should have to say so.
    /// </summary>
    public static bool FourNameTheOperationAndOneNamesNothing(string core)
    {
        string body = Body(core);

        // Counted by the message, not by the macro: a hexdump takes CHIAKI_LOG_ERROR as an argument
        // and would be counted as another line by anything matching the prefix alone.
        int lines = body.Split("session->log, \"", StringSplitOptions.None).Length - 1;
        int operation = body.Split("\"check_candidates: ", StringSplitOptions.None).Length - 1;

        return lines == 5 && operation == 4;
    }

    /// <summary>The one line carrying no name of any kind.</summary>
    public const string TheLineThatNamesNothing = "Received an extra response, ignoring....";

    /// <summary>Whether it is still there, unqualified.</summary>
    public static bool TheUnnamedLineIsStillThere(string core)
        => Body(core).Contains($"\"{TheLineThatNamesNothing}\"", StringComparison.Ordinal);

    /// <summary>And whether the request it reads is still the probe's size.</summary>
    public static bool TheRequestIsStillTheProbesSize(string core)
        => Body(core).Contains(
            $"uint8_t req[{FollowupExchange.RequestLength}] = {{0}};", StringComparison.Ordinal);

    /// <summary>receive_request_send_response_ps's body.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason ten earlier tasks each wrote down.
        int start = text.LastIndexOf(
            "static ChiakiErrorCode receive_request_send_response_ps(Session *session",
            StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("\nstatic void log_session_state(", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
