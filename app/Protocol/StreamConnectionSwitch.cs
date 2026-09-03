using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Why the session thread came out of the switch wait.</summary>
public enum SwitchWake
{
    /// <summary>The ack arrived and ctrl set the flag. The only one that proceeds.</summary>
    AckReceived,

    /// <summary>Somebody asked the session to stop.</summary>
    Stopped,

    /// <summary>Ctrl died while the session was waiting on it.</summary>
    CtrlFailed,

    /// <summary>Nothing woke it and the five seconds ran out.</summary>
    TimedOut,

    /// <summary>A stop arrived and the ack was already in. The one case the stop check reaches.</summary>
    StoppedAfterAck,
}

/// <summary>What the session thread then does, and what it says it did.</summary>
public enum SwitchOutcome
{
    /// <summary>On to the handshake key and the stream connection.</summary>
    Proceed,

    /// <summary>Out through quit_ctrl, logging that the ack did not arrive.</summary>
    ReportedAsMissingAck,

    /// <summary>Out through quit_ctrl with the quit reason recorded as a stop.</summary>
    Stopped,
}

/// <summary>
/// PP28, the second of the three joins: the switch to the stream connection, and its wait.
///
/// On the rudp path - and only there - the session tells the console it is switching, then waits for
/// ctrl to acknowledge it from ctrl's own thread. <c>CtrlOnceOnly</c> models the setting side. This
/// is the waiting side, and the waiting side has a defect in it.
///
/// THE PREDICATE WAKES ON THREE THINGS AND THE CHECK READS ONE. It returns true for should_stop, for
/// ctrl_failed, or for the flag ctrl sets. The line after the wait tests the FLAG alone - so a stop,
/// a dead ctrl and a five-second timeout all fall into one arm and all three log "Failed to receive
/// switch to stream connection ack!". Two of those three did receive no ack because nobody was
/// asking any more, which is a different thing from a console that did not answer.
///
/// AND THE QUIT REASON IS WRONG FOR THE STOP. CHECK_STOP is the macro that records
/// CHIAKI_QUIT_REASON_STOPPED, and here it sits AFTER the flag check - so on a stop the flag is
/// false, the ack arm quits first, and the stop check never runs. The session ends without ever
/// recording that it was stopped. The one case CHECK_STOP does reach is the race where the ack
/// arrived AND a stop is pending, which is <see cref="SwitchWake.StoppedAfterAck"/>.
///
/// THE WAIT'S OWN RETURN CODE IS DISCARDED, and the C says so structurally: the block declares a
/// second <c>ChiakiErrorCode err</c> that shadows the function's, assigns the wait's result to it,
/// and never reads it. So the timeout is not distinguished from a spurious wake by anything except
/// the flag - which is correct, and worth stating, because a port that checked the returned code
/// would add a distinction the C does not make.
///
/// This is modelled and NOT repaired. Splitting the arm would change what a session reports, which
/// is behaviour, and "no redesign while porting" is a non-goal - the port reproduces the log the C
/// writes. Recording it is what makes it a decision later rather than a discovery.
/// </summary>
public static class StreamConnectionSwitch
{
    /// <summary>The file the whole step is read from.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>It, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>SESSION_EXPECT_TIMEOUT_MS, which is what the wait is given.</summary>
    public const int TimeoutMilliseconds = 5000;

    /// <summary>
    /// The three fields the predicate reads, in the order it reads them.
    ///
    /// All three, because the count is the finding: a predicate reading one field would make the
    /// check after it exact.
    /// </summary>
    public static IReadOnlyList<string> PredicateFields { get; } =
        ["should_stop", "ctrl_failed", "stream_connection_switch_received"];

    /// <summary>
    /// Whether this step happens at all. The whole block is inside <c>if(session->rudp)</c>, so a
    /// session that is not on the rudp path goes straight to the handshake key.
    /// </summary>
    public static bool Happens(bool rudp) => rudp;

    /// <summary>
    /// What each wake produces, which is the join.
    ///
    /// Three of the five land on one outcome and that is the point of the model: the arm is chosen
    /// by the flag rather than by what happened, so the C cannot tell them apart and neither does
    /// this.
    /// </summary>
    public static SwitchOutcome After(SwitchWake wake) => wake switch
    {
        SwitchWake.AckReceived => SwitchOutcome.Proceed,
        SwitchWake.StoppedAfterAck => SwitchOutcome.Stopped,
        _ => SwitchOutcome.ReportedAsMissingAck,
    };

    /// <summary>
    /// Whether the session records that it was stopped.
    ///
    /// Only where the ack arrived first. On a plain stop the missing-ack arm quits before CHECK_STOP
    /// is reached, so the quit reason stays whatever it already was - and on the ordinary path that
    /// is nothing at all.
    /// </summary>
    public static bool RecordsAStop(SwitchWake wake) => wake == SwitchWake.StoppedAfterAck;

    /// <summary>
    /// The wakes that end the session while telling the user the console did not acknowledge.
    ///
    /// Named rather than counted, because which ones they are is the whole of what a later decision
    /// about this arm would be taken on.
    /// </summary>
    public static IReadOnlyList<SwitchWake> Misreported { get; } =
        [SwitchWake.Stopped, SwitchWake.CtrlFailed, SwitchWake.TimedOut];
}

/// <summary>
/// PP28: the switch step where session.c states it.
/// </summary>
public static class StreamConnectionSwitchSource
{
    /// <summary>Whether the step is still inside the rudp guard.</summary>
    public static bool OnlyOnTheRudpPath(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int guard = CCall.Mark(compact, "if(session->rudp)");
        if (guard < 0)
            return false;

        return CCall.At(compact, "chiaki_rudp_send_switch_to_stream_connection_message(", guard) > guard;
    }

    /// <summary>Whether the predicate still reads all three fields.</summary>
    public static bool ThePredicateReadsThreeFields(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int start = CCall.Mark(compact, "session_check_state_pred_stream_connection_switch(void *user)");
        if (start < 0)
            return false;

        int end = CCall.Mark(compact, "static bool session_check_state_pred_regist", start);
        if (end < 0)
            return false;

        string body = compact[start..end];
        return StreamConnectionSwitch.PredicateFields.All(
            field => body.Contains(field, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the arm after the wait still tests the flag rather than the wait's own result.
    ///
    /// The negation is the shape: <c>if(!session->stream_connection_switch_received)</c>. A port
    /// that tested the error code would be making a distinction this does not.
    /// </summary>
    public static bool TheArmTestsTheFlagAndNotTheError(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.InOrder(
            CCall.Compact(source),
            "chiaki_cond_timedwait_pred(&session->state_cond, &session->state_mutex, SESSION_EXPECT_TIMEOUT_MS,",
            "if(!session->stream_connection_switch_received)");
    }

    /// <summary>
    /// Whether the stop check still comes AFTER that arm, which is what loses the quit reason.
    ///
    /// The order is the defect and the order is what this holds. Moving CHECK_STOP above the flag
    /// test would record the stop - and would change what a stopped session reports, which is why
    /// the port reproduces the order rather than improving it.
    /// </summary>
    public static bool TheStopCheckComesAfterTheAckArm(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string compact = CCall.Compact(source);

        int arm = CCall.Mark(compact, "if(!session->stream_connection_switch_received)");
        if (arm < 0)
            return false;

        int stop = CCall.Mark(compact, "CHECK_STOP(quit_ctrl);", arm);
        int run = CCall.At(compact, "chiaki_stream_connection_run(", arm);

        return stop > arm && (run < 0 || stop < run);
    }

    /// <summary>
    /// Whether the timeout is still the constant this model carries.
    ///
    /// Read off the raw source and split on whitespace rather than matched as one string: the
    /// define is spelled with tabs, and how many is a checkout's business rather than this check's.
    /// </summary>
    public static bool TheTimeoutIsUnchanged(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int at = source.IndexOf("#define SESSION_EXPECT_TIMEOUT_MS", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = source.IndexOf('\n', at);
        string line = end < 0 ? source[at..] : source[at..end];

        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Contains(
                StreamConnectionSwitch.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
    }
}
