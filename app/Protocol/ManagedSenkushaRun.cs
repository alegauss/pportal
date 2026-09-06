using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// What senkusha's run needs from the world, named for the C calls each stands in for.
///
/// Every member is one thing chiaki_senkusha_run does to something outside itself, so a host that
/// records its calls produces the run's TRACE - and the trace is what an ordering is asserted over.
/// A host that answers false or leaves a flag down is how each failure path is reached without a
/// console.
/// </summary>
public interface ISenkushaRunHost
{
    /// <summary>should_stop, which the run reads before it opens anything at all.</summary>
    bool ShouldStop { get; }

    /// <summary>The state assignment and the two clears, which the C writes as one triple.</summary>
    void BeginState(SenkushaState state);

    /// <summary>chiaki_takion_connect. False is the failure that must NOT close a takion.</summary>
    bool ConnectTakion();

    /// <summary>chiaki_cond_timedwait_pred on state_finished_cond_check, for one state.</summary>
    /// <returns>The flags as they read when the wait returned, and whether it returned by timeout.</returns>
    (SenkushaWaitState Flags, bool TimedOut) Wait(SenkushaState state);

    /// <summary>senkusha_set_version, whose ack the protocol state waits for.</summary>
    bool SetVersion();

    /// <summary>senkusha_send_big, which is not the stream's BIG and carries no launch spec.</summary>
    bool SendBig();

    /// <summary>senkusha_run_rtt_test over its ten pings.</summary>
    ChiakiError RunRttTest(out ulong roundTripMicroseconds);

    /// <summary>senkusha_run_mtu_in_test, given the timeout the round trip derived.</summary>
    ChiakiError RunMtuInTest(ulong timeoutMs, out uint mtuIn);

    /// <summary>senkusha_run_mtu_out_test, which starts where the inbound one finished.</summary>
    ChiakiError RunMtuOutTest(uint mtuIn, ulong timeoutMs, out uint mtuOut);

    /// <summary>senkusha_send_disconnect, from the label. Its answer is logged and not acted on.</summary>
    void SendDisconnect();

    /// <summary>chiaki_takion_close, which every path past a successful connect passes through.</summary>
    void CloseTakion();
}

/// <summary>How far senkusha's walk got, which its error code does not say.</summary>
public enum SenkushaRung
{
    /// <summary>Nothing: the stop was already set when the run began.</summary>
    Start,

    /// <summary>The takion connected and its event arrived.</summary>
    TakionConnected,

    /// <summary>And the console acknowledged the protocol version.</summary>
    ProtocolAcked,

    /// <summary>And it answered the BIG with a bang.</summary>
    BangAwaited,

    /// <summary>And the round trip is measured, which is what the two searches are timed by.</summary>
    RttMeasured,

    /// <summary>And the inbound MTU, which the launch spec carries.</summary>
    MtuInMeasured,

    /// <summary>And the outbound one, which is the whole of what senkusha is for.</summary>
    MtuOutMeasured,
}

/// <summary>Which exit label a run left by, and therefore what it did on the way out.</summary>
public enum SenkushaExit
{
    /// <summary>`quit`: nothing was opened, so nothing is closed and nobody is told.</summary>
    Quit,

    /// <summary>`quit_takion`: the takion is closed and the console is NOT told.</summary>
    CloseOnly,

    /// <summary>`disconnect`: the console is told, and then the takion is closed.</summary>
    Disconnected,
}

/// <summary>What a whole run measured and how it ended.</summary>
/// <param name="Error">What the C returns from the same exit.</param>
/// <param name="Exit">Which of the three labels it left by.</param>
/// <param name="Rung">How far the walk got.</param>
/// <param name="RoundTripMicroseconds">The round trip, where the test that measures it ran.</param>
/// <param name="MtuIn">The inbound MTU, likewise.</param>
/// <param name="MtuOut">And the outbound one.</param>
public readonly record struct SenkushaRunReading(
    ChiakiError Error,
    SenkushaExit Exit,
    SenkushaRung Rung,
    ulong RoundTripMicroseconds,
    uint MtuIn,
    uint MtuOut);

/// <summary>
/// PP790, under PP784: chiaki_senkusha_run as a sequence, driving the models PP788 and PP789 wrote
/// apart.
///
/// Those two say what the states are and what the measurements mean. Neither is a run. This is: one
/// function that walks the states with <see cref="SenkushaStates"/>, derives the search timeout the
/// way <see cref="SenkushaMeasurements.MtuTimeoutMs"/> does, and leaves by one of three labels.
///
/// THE ORDERING IS THE DELIVERABLE, which is PP295's sentence for the stream connection and is
/// truer here: senkusha's whole output is three numbers, and a run that produced them in some other
/// order would produce different ones. There is no console in a gate, so the only way to assert a
/// sequence is to record what it ASKS and read the trace back.
///
/// THREE EXITS AND THEY ARE NOT THE SAME TEARDOWN. `quit` closes nothing - reached by the stop the
/// run reads before it opens anything, and by a connect that failed, which opened nothing to close.
/// `quit_takion` closes the takion and tells the console NOTHING: every failure between the connect
/// and the bang leaves that way. `disconnect` tells the console and then closes, and it is reached
/// by the three measurements' failures AND by success - so the happy path and a failed MTU test
/// leave identically, which is the shape PP379 was filed about one call in.
///
/// SO A CONSOLE IS TOLD ONLY AFTER THE BANG. A senkusha that timed out waiting for the protocol ack
/// closes its socket and says nothing, and the session then opens a stream connection to a console
/// still holding the last conversation. PP379 says why that matters more here than in the stream:
/// the refusal arrives later, at a different call, with nothing pointing back.
/// </summary>
public static class ManagedSenkushaRun
{
    /// <summary>The bounds both searches are given, which the run passes and does not choose.</summary>
    public const uint SearchMin = SenkushaMeasurements.MtuMin;

    /// <inheritdoc cref="SearchMin"/>
    public const uint SearchMax = SenkushaMeasurements.MtuMax;

    /// <summary>And the attempts per step.</summary>
    public const uint SearchRetries = SenkushaMeasurements.MtuRetries;

    /// <summary>The run, answering what the C answers from the same exit.</summary>
    public static SenkushaRunReading Run(ISenkushaRunHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        // Before anything is opened. A stop already set here is CANCELED with no takion and no
        // disconnect - the one path that leaves by `quit` without having tried.
        if (host.ShouldStop)
            return new SenkushaRunReading(ChiakiError.Canceled, SenkushaExit.Quit, SenkushaRung.Start, 0, 0, 0);

        host.BeginState(SenkushaState.TakionConnect);

        // A connect that FAILED opened nothing, so this leaves by `quit` and not by the label that
        // closes. The stream connection's own table got exactly this rung wrong (PP295).
        if (!host.ConnectTakion())
            return new SenkushaRunReading(ChiakiError.Unknown, SenkushaExit.Quit, SenkushaRung.Start, 0, 0, 0);

        if (Awaited(host, SenkushaState.TakionConnect) is { } connectFailed)
            return Close(host, connectFailed, SenkushaRung.Start);

        host.BeginState(SenkushaState.ExpectProtocolAck);

        if (!host.SetVersion())
            return Close(host, ChiakiError.Unknown, SenkushaRung.TakionConnected);

        if (Awaited(host, SenkushaState.ExpectProtocolAck) is { } versionFailed)
            return Close(host, versionFailed, SenkushaRung.TakionConnected);

        host.BeginState(SenkushaState.ExpectBang);

        if (!host.SendBig())
            return Close(host, ChiakiError.Unknown, SenkushaRung.ProtocolAcked);

        if (Awaited(host, SenkushaState.ExpectBang) is { } bangFailed)
            return Close(host, bangFailed, SenkushaRung.ProtocolAcked);

        // From here every exit tells the console, which is what the `disconnect:` label is.
        ChiakiError rtt = host.RunRttTest(out ulong roundTrip);
        if (rtt != ChiakiError.Success)
            return Disconnect(host, rtt, SenkushaRung.BangAwaited, roundTrip, 0, 0);

        // Derived once, between the test that measures it and the two that spend it.
        ulong timeoutMs = SenkushaMeasurements.MtuTimeoutMs(roundTrip);

        ChiakiError inbound = host.RunMtuInTest(timeoutMs, out uint mtuIn);
        if (inbound != ChiakiError.Success)
            return Disconnect(host, inbound, SenkushaRung.RttMeasured, roundTrip, 0, 0);

        ChiakiError outbound = host.RunMtuOutTest(mtuIn, timeoutMs, out uint mtuOut);
        if (outbound != ChiakiError.Success)
            return Disconnect(host, outbound, SenkushaRung.MtuInMeasured, roundTrip, mtuIn, 0);

        // And success leaves by the same label, which is the ordering worth stating: the happy path
        // and a failed measurement are indistinguishable from the console's side.
        return Disconnect(
            host, ChiakiError.Success, SenkushaRung.MtuOutMeasured, roundTrip, mtuIn, mtuOut);
    }

    /// <summary>
    /// One state's wait, answering null where it finished and the C's own code where it did not.
    ///
    /// The two arms the C spells four times over: a stop is CANCELED and anything else is UNKNOWN.
    /// PP380 put the second one there - the wait returns SUCCESS with the predicate false, so a
    /// run that carried `err` out reported success from a state that never finished.
    /// </summary>
    private static ChiakiError? Awaited(ISenkushaRunHost host, SenkushaState state)
    {
        (SenkushaWaitState flags, bool _) = host.Wait(state);

        if (SenkushaStates.WaitEnds(flags) && flags.Finished)
            return null;

        return flags.ShouldStop ? ChiakiError.Canceled : ChiakiError.Unknown;
    }

    /// <summary>The `quit_takion` label: close, and tell the console nothing.</summary>
    private static SenkushaRunReading Close(ISenkushaRunHost host, ChiakiError error, SenkushaRung rung)
    {
        host.CloseTakion();

        return new SenkushaRunReading(error, SenkushaExit.CloseOnly, rung, 0, 0, 0);
    }

    /// <summary>
    /// The `disconnect` label: tell the console, then close - and in that order.
    ///
    /// The disconnect's own answer is not read, which is PP379's finding and its decision both: a
    /// teardown cannot retry, and the error the run is carrying is the one that belongs to it.
    /// </summary>
    private static SenkushaRunReading Disconnect(
        ISenkushaRunHost host,
        ChiakiError error,
        SenkushaRung rung,
        ulong roundTrip,
        uint mtuIn,
        uint mtuOut)
    {
        host.SendDisconnect();
        host.CloseTakion();

        return new SenkushaRunReading(error, SenkushaExit.Disconnected, rung, roundTrip, mtuIn, mtuOut);
    }
}

/// <summary>
/// PP790: the run's sequence read out of senkusha.c, so the port cannot drift off the order.
/// </summary>
public static class ManagedSenkushaRunSource
{
    /// <summary>Where the run is.</summary>
    public const string RelativePath = SenkushaStatesSource.RelativePath;

    /// <summary>senkusha.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The run's body, or null where it is gone.</summary>
    public static string? RunBody(string source)
        => CFunction.Body(source, "CHIAKI_EXPORT ChiakiErrorCode chiaki_senkusha_run(");

    /// <summary>
    /// The three labels the run declares, which are the three teardowns.
    ///
    /// Named rather than counted: what a port has to reproduce is WHICH one each failure takes, and
    /// a count says nothing about that.
    /// </summary>
    public static IReadOnlyList<string> Labels { get; } = ["disconnect:", "quit_takion:", "quit:"];

    /// <summary>
    /// Whether the three labels are still in the order that makes the cascade fall through.
    ///
    /// disconnect falls into quit_takion falls into quit, so telling the console also closes and
    /// closing also returns. Reordering them would make a disconnect skip the close.
    /// </summary>
    public static bool TheLabelsStillCascade(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        var at = -1;

        foreach (string label in Labels)
        {
            int next = runBody.IndexOf($"\n{label}", at + 1, StringComparison.Ordinal);
            if (next <= at)
                return false;

            at = next;
        }

        return true;
    }

    /// <summary>
    /// Whether a failed connect still leaves by `quit` rather than by the label that closes.
    ///
    /// The rung PP295 found the stream connection's own table wrong about, in the same place: a
    /// connect that answered an error opened nothing, and closing it would be closing a takion that
    /// never came up.
    /// </summary>
    public static bool AFailedConnectStillSkipsTheClose(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int connect = runBody.IndexOf("err = chiaki_takion_connect(", StringComparison.Ordinal);
        if (connect < 0)
            return false;

        int quit = runBody.IndexOf("QUIT(quit);", connect, StringComparison.Ordinal);
        int closes = runBody.IndexOf("QUIT(quit_takion);", connect, StringComparison.Ordinal);

        return quit > connect && (closes < 0 || quit < closes);
    }

    /// <summary>
    /// Whether the console is still told only from the bang onwards.
    ///
    /// Every failure before the three measurements takes quit_takion, so the disconnect below is
    /// unreachable from them. A port that told the console earlier would be politer than the C and
    /// would be sending a message on a takion the C has already closed.
    /// </summary>
    public static bool TheConsoleIsStillToldOnlyAfterTheBang(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int bang = runBody.IndexOf("Senkusha successfully received bang", StringComparison.Ordinal);
        if (bang < 0)
            return false;

        // No path before the bang reaches the disconnect label.
        return !runBody[..bang].Contains("goto disconnect;", StringComparison.Ordinal)
            && runBody[bang..].Contains("goto disconnect;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the three measurements still run in the order that lets two of them be timed.
    ///
    /// The round trip first, because the two searches spend it. Running a search first would give
    /// it a timeout derived from a number nobody has.
    /// </summary>
    public static bool TheMeasurementsStillRunInOrder(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        int rtt = runBody.IndexOf("senkusha_run_rtt_test(", StringComparison.Ordinal);
        int derived = runBody.IndexOf("mtu_timeout_ms = ", StringComparison.Ordinal);
        int inbound = runBody.IndexOf("senkusha_run_mtu_in_test(", StringComparison.Ordinal);
        int outbound = runBody.IndexOf("senkusha_run_mtu_out_test(", StringComparison.Ordinal);

        return rtt >= 0 && derived > rtt && inbound > derived && outbound > inbound;
    }

    /// <summary>Whether the outbound search is still handed what the inbound one found.</summary>
    public static bool TheOutboundSearchStillTakesTheInboundAnswer(string runBody)
    {
        ArgumentNullException.ThrowIfNull(runBody);

        return runBody.Contains("senkusha_run_mtu_out_test(senkusha, *mtu_in,", StringComparison.Ordinal);
    }
}
