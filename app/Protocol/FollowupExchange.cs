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
/// A RECEIVE THAT FAILS SPINS. There are four ways out of this loop: a timeout, a select error, a
/// packet of the wrong size, and a message of the wrong type. A failed receive is none of them. It
/// logs and continues, back to a wait that reports the socket readable, back to a receive that
/// fails again. The socket was connected before this call, so on Windows an ICMP rejection arrives
/// as a receive error on a socket select calls readable - no progress, no timeout, one log line per
/// turn. <see cref="Leaves"/> answers which steps end the loop, and <see cref="FollowupStep.Retry"/>
/// is the one that does not.
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
    /// THE FINDING. Whether a failed receive still continues rather than leaving.
    ///
    /// True means the spin is present, which is what this asserts rather than a fix.
    /// </summary>
    public static bool AFailedReceiveStillContinues(string core)
    {
        string body = Body(core);

        int fails = body.IndexOf(
            "check_candidates: Receiving response from %s:%d failed with error: ", StringComparison.Ordinal);
        if (fails < 0)
            return false;

        int closes = body.IndexOf("\n        }", fails, StringComparison.Ordinal);
        if (closes < 0)
            return false;

        return body[fails..closes].Contains("continue;", StringComparison.Ordinal)
            && !body[fails..closes].Contains("return", StringComparison.Ordinal);
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
    /// Whether three lines still name the OPERATION and one still names nothing.
    ///
    /// The three are defensible - PP238 settled that - so what this asserts is the split, not a
    /// count of wrongs. The fourth is the one with nothing on it.
    /// </summary>
    public static bool ThreeNameTheOperationAndOneNamesNothing(string core)
    {
        string body = Body(core);

        // Counted by the message, not by the macro: a hexdump takes CHIAKI_LOG_ERROR as an argument
        // and would be counted as a fifth line by anything matching the prefix alone.
        int lines = body.Split("session->log, \"", StringSplitOptions.None).Length - 1;
        int operation = body.Split("\"check_candidates: ", StringSplitOptions.None).Length - 1;

        return lines == 4 && operation == 3;
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
