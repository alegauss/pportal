using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which state the stream connection is walking through.</summary>
public enum StreamState
{
    /// <summary>Waiting for takion to come up.</summary>
    TakionConnect,

    /// <summary>Big has been sent; waiting for the console's bang.</summary>
    ExpectBang,

    /// <summary>Bang is in; waiting for streaminfo.</summary>
    ExpectStreaminfo,

    /// <summary>A stream exists. From here the loop only sends heartbeats.</summary>
    Idle,
}

/// <summary>What the state machine does next.</summary>
public enum StreamStep
{
    /// <summary>The state finished. Move to the next one.</summary>
    Advance,

    /// <summary>Keep waiting - nothing has happened yet.</summary>
    Wait,

    /// <summary>Somebody asked the stream to stop.</summary>
    Stopped,

    /// <summary>The state reported failure, or its wait ran out.</summary>
    Failed,
}

/// <summary>What the wait can see.</summary>
/// <param name="Finished">The state got what it was waiting for.</param>
/// <param name="ShouldStop">chiaki_stream_connection_stop was called.</param>
/// <param name="RemoteDisconnected">The console went away.</param>
/// <param name="Failed">
/// PP365: written by three handlers and read by NOTHING - not the predicate, not the run. Carried
/// here so the port can assert that it is dead rather than quietly grow a use for it.
/// </param>
public readonly record struct StreamWaitState(
    bool Finished = false,
    bool ShouldStop = false,
    bool RemoteDisconnected = false,
    bool Failed = false);

/// <summary>
/// PP362, under PP295: the three states a stream connection walks before a stream exists.
///
/// Each is the same four steps - set the state, clear finished and failed, do the thing, wait - and
/// the wait is what makes this a machine rather than a sequence. state_finished_cond_check watches
/// all three flags together, so a wait that returns has said nothing until they are re-read: the
/// same shape as session.c's five predicates, and the same trap. A caller treating "the wait
/// returned" as "the thing happened" walks into a stream with no streaminfo.
///
/// ONE MESSAGE CAN ARRIVE BEFORE IT IS EXPECTED. Streaminfo landing while the state is still
/// EXPECT_BANG is buffered rather than dropped, and when the state moves the handler is called on
/// that buffer DIRECTLY - then the wait is skipped where that already finished the state. Without it
/// a console answering faster than the client can change state deadlocks for the whole timeout and
/// then reports not receiving a message it received.
///
/// NOTHING ELSE GETS THAT TREATMENT. A bang arriving early is not buffered, which is worth stating
/// because the asymmetry looks like an oversight and is the behaviour.
/// </summary>
public static class StreamConnectionStates
{
    /// <summary>The order the states are walked in, before the stream exists.</summary>
    public static IReadOnlyList<StreamState> Walk { get; } =
        [StreamState.TakionConnect, StreamState.ExpectBang, StreamState.ExpectStreaminfo];

    /// <summary>
    /// Whether a message arriving before its state is buffered for replay.
    ///
    /// Only streaminfo. See the note on the class for why the asymmetry is stated rather than
    /// smoothed.
    /// </summary>
    public static bool IsBufferedWhenEarly(StreamState state) => state == StreamState.ExpectStreaminfo;

    /// <summary>
    /// Whether the wait ends, by the predicate the C actually uses.
    ///
    /// PP365: finished, stopped or the remote going away. NOT state_failed - a handler that fails
    /// sets a flag nobody watches, so the wait runs its full timeout.
    /// </summary>
    public static bool WaitEnds(StreamWaitState flags)
        => flags.Finished || flags.ShouldStop || flags.RemoteDisconnected;

    /// <summary>
    /// What the machine does, given what the flags say and whether the wait timed out.
    /// </summary>
    public static StreamStep Next(StreamWaitState flags, bool waitTimedOut)
    {
        // Stop first, as it is at every wait site in these files.
        if (flags.ShouldStop)
            return StreamStep.Stopped;

        if (flags.Finished)
            return StreamStep.Advance;

        // PP365: everything else is one answer, and the C's log line says so - "didn't receive bang
        // OR failed to handle it". It cannot tell the two apart, because the flag that would have
        // told it is never read. A handler that failed and a console that never answered arrive
        // here identically, one of them after the whole timeout.
        return waitTimedOut || flags.RemoteDisconnected ? StreamStep.Failed : StreamStep.Wait;
    }

    /// <summary>
    /// Whether the failure flag changes anything, which it does not.
    ///
    /// Asserted rather than omitted: a port that grew a use for it would be a port that reports
    /// failures sooner than the C does, which is better behaviour and different behaviour.
    /// </summary>
    public static bool FailureFlagIsRead => false;

    /// <summary>
    /// Whether the state needs to wait at all, having replayed anything buffered.
    ///
    /// The C tests state_finished after the replay and skips the wait where it is already true.
    /// </summary>
    public static bool WaitsAfterReplay(StreamWaitState flags) => !flags.Finished;

    /// <summary>The state that follows this one, or null where a stream already exists.</summary>
    public static StreamState? After(StreamState state) => state switch
    {
        StreamState.TakionConnect => StreamState.ExpectBang,
        StreamState.ExpectBang => StreamState.ExpectStreaminfo,
        StreamState.ExpectStreaminfo => StreamState.Idle,
        _ => null,
    };
}

/// <summary>
/// PP362: the walk held against streamconnection.c. PP297's capture cannot judge it - the tap sits
/// on ctrl and the session request, and none of this crosses either.
/// </summary>
public static class StreamConnectionStatesSource
{
    /// <summary>Where the walk lives.</summary>
    public const string RelativePath = @"lib\src\streamconnection.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The run's body, or null.</summary>
    public static string? RunBody(string filePath)
        => CFunction.BodyIn(filePath, "chiaki_stream_connection_run");

    /// <summary>
    /// Whether every state still clears both flags when it is entered.
    ///
    /// A state entered without clearing them inherits the previous state's answer, and the wait
    /// after it returns immediately with the wrong verdict.
    /// </summary>
    public static bool EveryStateStillClearsBothFlags(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        var entries = 0;
        for (int at = runBody.IndexOf("stream_connection->state = STATE_", StringComparison.Ordinal);
             at >= 0;
             at = runBody.IndexOf("stream_connection->state = STATE_", at + 1, StringComparison.Ordinal))
        {
            // The two clears follow the assignment, before anything else happens.
            int finished = runBody.IndexOf("state_finished = false;", at, StringComparison.Ordinal);
            int failed = runBody.IndexOf("state_failed = false;", at, StringComparison.Ordinal);
            int next = runBody.IndexOf("stream_connection->state = STATE_", at + 1, StringComparison.Ordinal);

            bool bothBefore = finished > at && failed > finished
                && (next < 0 || failed < next);

            if (!bothBefore)
                return false;

            entries++;
        }

        // Four entries: the three states plus idle.
        return entries == 4;
    }

    /// <summary>
    /// Whether the predicate still watches the three it watches - and still not the failure flag.
    ///
    /// PP365: the first version of this check asserted state_failed was among them, which is what a
    /// reader assumes from its name and from the four places it is cleared. It is not, and the check
    /// failing is how that was found.
    /// </summary>
    public static bool ThePredicateStillWatchesTheThree(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "static bool state_finished_cond_check");
        if (body is null)
            return false;

        return body.Contains("state_finished", StringComparison.Ordinal)
            && body.Contains("should_stop", StringComparison.Ordinal)
            && body.Contains("remote_disconnected", StringComparison.Ordinal)
            && !body.Contains("state_failed", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the failure flag is still written and never read.
    ///
    /// Counted both ways: assignments to it, and any use that is not an assignment. The second must
    /// be none.
    /// </summary>
    public static bool TheFailureFlagIsStillDead(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var writes = 0;
        var reads = 0;

        for (int at = source.IndexOf("state_failed", StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf("state_failed", at + 1, StringComparison.Ordinal))
        {
            int lineEnd = source.IndexOf('\n', at);
            string rest = lineEnd < 0 ? source[at..] : source[at..lineEnd];

            if (rest.Contains(" = ", StringComparison.Ordinal))
                writes++;
            else
                reads++;
        }

        return writes > 0 && reads == 0;
    }

    /// <summary>
    /// Whether early streaminfo is still replayed, and the wait still skipped where it finished.
    ///
    /// Both halves: the handler is called on the buffer directly, and the wait below is guarded on
    /// the state not already being finished.
    /// </summary>
    public static bool EarlyStreaminfoIsStillReplayed(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int buffered = runBody.IndexOf("if(stream_connection->streaminfo_early_buf)", StringComparison.Ordinal);
        if (buffered < 0)
            return false;

        int replay = runBody.IndexOf(
            "stream_connection_takion_data_expect_streaminfo(stream_connection, stream_connection->streaminfo_early_buf",
            buffered, StringComparison.Ordinal);
        int guard = runBody.IndexOf("if(!stream_connection->state_finished)", replay < 0 ? buffered : replay,
            StringComparison.Ordinal);

        return replay > buffered && guard > replay;
    }
}
