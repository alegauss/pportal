using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What the idle loop does when its wait returns.</summary>
public enum IdleStep
{
    /// <summary>The wait timed out, which is the work: send a heartbeat and wait again.</summary>
    SendHeartbeat,

    /// <summary>Anything else. The loop is left and the stream ends.</summary>
    Leave,
}

/// <summary>
/// PP363, under PP295: the loop where a timeout is the work, and the inverse of the one beside it.
///
/// Once a stream exists, chiaki_stream_connection_run sits in a loop with one job: wait a second,
/// and if the wait TIMED OUT, send a heartbeat and wait again. Anything that is not a timeout leaves
/// the loop and the stream ends.
///
/// SO SUCCESS IS THE EXIT. The predicate it waits on is state_finished_cond_check, and that
/// returning true means finished, stopped or the console gone - none of which a live stream wants.
/// PP349's ctrl loop is the mirror image: there CANCELED is the work branch and everything else is
/// failure. Two loops, two files, both a condition wait, and the return value meaning "carry on" is
/// the opposite one in each. A port writing the second from memory of the first either stops sending
/// heartbeats or spins, and both compile.
///
/// A HEARTBEAT THAT FAILS TO SEND IS LOGGED AND IGNORED. The loop carries on and waits again, so a
/// stream whose heartbeats are all failing looks alive from in here until the console gives up on
/// it. That is the right behaviour for a diagnostic message and it is deliberate rather than
/// inherited - PP370's disconnect is the other exception in this file, and it logs too.
///
/// AND THE RUN'S OWN ERROR CODE IS DECIDED AFTER THE LOOP, at the disconnect label, by three tests
/// in order: should_stop wins and gives CANCELED, then remote_disconnected gives DISCONNECTED, and
/// otherwise whatever err already held stands. PP336 ported what the SESSION makes of those three;
/// this is where they come from.
/// </summary>
public static class StreamIdleLoop
{
    /// <summary>HEARTBEAT_INTERVAL_MS: how long the idle wait is given.</summary>
    public const int HeartbeatIntervalMs = 1000;

    /// <summary>EXPECT_TIMEOUT_MS: what the three states before it wait, for contrast.</summary>
    public const int ExpectTimeoutMs = 5000;

    /// <summary>
    /// What the loop does, given what its wait returned.
    ///
    /// One test and it is on TIMEOUT, so every other code - success, cancelled, a broken wait -
    /// takes the same exit. Written as the C writes it rather than as a switch over the codes that
    /// can occur, because which ones can occur is not what the loop asks.
    /// </summary>
    public static IdleStep Next(ChiakiError wait)
        => wait == ChiakiError.Timeout ? IdleStep.SendHeartbeat : IdleStep.Leave;

    /// <summary>
    /// Whether a failed heartbeat ends the loop, which it does not.
    ///
    /// Asserted rather than omitted, for the reason PP365's dead flag is: a port that ended the
    /// stream here would be a port that gives up sooner than the C does, which is a different
    /// product and not a tidier one.
    /// </summary>
    public static bool AFailedHeartbeatEndsTheLoop => false;

    /// <summary>
    /// What the run answers, given what the loop left in <paramref name="held"/> and what the two
    /// flags say at the disconnect label.
    ///
    /// The order is the whole of it. A stop beats a remote disconnect, and both beat whatever the
    /// loop's last wait returned - so a stream asked to stop while the console was going away
    /// reports CANCELED, and PP336's table then reads that as an ordinary ending.
    /// </summary>
    public static ChiakiError Outcome(ChiakiError held, bool shouldStop, bool remoteDisconnected)
    {
        if (shouldStop)
            return ChiakiError.Canceled;

        if (remoteDisconnected)
            return ChiakiError.Disconnected;

        return held;
    }

    /// <summary>
    /// What the loop leaves behind for <see cref="Outcome"/> when it exits on its own.
    ///
    /// The wait's code, unmodified. The success path past the loop assigns CHIAKI_ERR_SUCCESS
    /// before falling into the label, so a stream that ended because its predicate came true and
    /// neither flag is set answers SUCCESS.
    /// </summary>
    public static ChiakiError HeldOnLeaving(ChiakiError wait) => wait;
}

/// <summary>
/// PP363: the loop held against streamconnection.c, and against the ctrl loop it inverts.
///
/// PP297's capture cannot judge any of this - the tap sits on ctrl and the session request, and a
/// heartbeat on the stream connection crosses neither.
/// </summary>
public static class StreamIdleLoopSource
{
    /// <summary>Where the idle loop lives.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The run's body, or null.</summary>
    public static string? RunBody(string filePath)
        => CFunction.BodyIn(filePath, "chiaki_stream_connection_run");

    /// <summary>
    /// Whether a timeout is still the branch that carries on.
    ///
    /// The test and the send together: a loop testing TIMEOUT that no longer sends a heartbeat is
    /// as wrong as one that inverted the test, and the two are one edit apart.
    /// </summary>
    public static bool ATimeoutIsStillTheWorkBranch(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int loop = runBody.IndexOf("HEARTBEAT_INTERVAL_MS", StringComparison.Ordinal);
        if (loop < 0)
            return false;

        int test = runBody.IndexOf("if(err != CHIAKI_ERR_TIMEOUT)", loop, StringComparison.Ordinal);
        int leave = runBody.IndexOf("break;", test < 0 ? loop : test, StringComparison.Ordinal);
        int beat = runBody.IndexOf("stream_connection_send_heartbeat(", loop, StringComparison.Ordinal);

        return test > loop && leave > test && beat > leave;
    }

    /// <summary>
    /// Whether a failed heartbeat is still logged and ignored.
    ///
    /// What is looked for is what is NOT there: no break and no goto between the failure test and
    /// the end of the loop. The log alone would pass a check that only asked for one.
    /// </summary>
    public static bool AFailedHeartbeatIsStillIgnored(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int beat = runBody.IndexOf("stream_connection_send_heartbeat(", StringComparison.Ordinal);
        if (beat < 0)
            return false;

        int test = runBody.IndexOf("if(err != CHIAKI_ERR_SUCCESS)", beat, StringComparison.Ordinal);
        int logged = runBody.IndexOf("failed to send heartbeat", beat, StringComparison.Ordinal);
        if (test < 0 || logged < test)
            return false;

        // The arm is one log line: the next statement after it is the else, not an exit.
        int close = runBody.IndexOf('}', logged);
        string arm = close < 0 ? runBody[test..] : runBody[test..close];

        return !arm.Contains("break;", StringComparison.Ordinal)
            && !arm.Contains("goto ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the disconnect label still decides the code in the order this port models.
    ///
    /// should_stop, then remote_disconnected, and nothing after them - so whatever the loop left
    /// stands where neither holds.
    /// </summary>
    public static bool TheOutcomeIsStillDecidedInThatOrder(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int label = runBody.IndexOf("disconnect:", StringComparison.Ordinal);
        if (label < 0)
            return false;

        int stop = runBody.IndexOf("if(stream_connection->should_stop)", label, StringComparison.Ordinal);
        int canceled = runBody.IndexOf("err = CHIAKI_ERR_CANCELED;", stop < 0 ? label : stop, StringComparison.Ordinal);
        int remote = runBody.IndexOf(
            "else if(stream_connection->remote_disconnected)", canceled < 0 ? label : canceled, StringComparison.Ordinal);
        int disconnected = runBody.IndexOf(
            "err = CHIAKI_ERR_DISCONNECTED;", remote < 0 ? label : remote, StringComparison.Ordinal);

        return stop > label && canceled > stop && remote > canceled && disconnected > remote;
    }
}
