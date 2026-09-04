namespace ChiakiNg.Protocol;

/// <summary>How one receive ended.</summary>
public enum TakionReceiveOutcome
{
    /// <summary>A datagram arrived.</summary>
    Datagram,

    /// <summary>Nothing arrived inside the timeout. CHIAKI_ERR_TIMEOUT.</summary>
    Timeout,

    /// <summary>Anything else, which is how the thread ends.</summary>
    Failed,
}

/// <summary>What a receive produced.</summary>
/// <param name="Outcome">Which of the three.</param>
/// <param name="Length">How many bytes arrived, meaningful only for a datagram.</param>
public readonly record struct TakionReceiveResult(TakionReceiveOutcome Outcome, int Length);

/// <summary>One thing the loop did, in the order it did it.</summary>
public enum TakionLoopStep
{
    /// <summary>The MACs of everything already queued were re-checked, the cipher having appeared.</summary>
    RecheckMacs,

    /// <summary>The packets held back until the cipher existed were handed on.</summary>
    FlushPostponed,

    /// <summary>The AV queues were flushed because the next timeout was already due.</summary>
    FlushOnZeroTimeout,

    /// <summary>A receive was attempted.</summary>
    Receive,

    /// <summary>The AV queues were flushed because the receive timed out.</summary>
    FlushOnTimeout,

    /// <summary>A datagram was handed to the handler.</summary>
    Dispatch,
}

/// <summary>What a run of the loop did and how it ended.</summary>
/// <param name="Trace">Every step, in order.</param>
/// <param name="Iterations">How many times round.</param>
/// <param name="ExitedOnFailure">Whether a failed receive ended it, which is the C's only exit.</param>
/// <param name="HitLimit">Whether the harness bound stopped it instead - see the limit's own note.</param>
public readonly record struct TakionLoopOutcome(
    IReadOnlyList<TakionLoopStep> Trace,
    int Iterations,
    bool ExitedOnFailure,
    bool HitLimit);

/// <summary>
/// What the receive thread asks of the world, so the loop can be run without one.
/// </summary>
public interface ITakionLoopHost
{
    /// <summary>Whether the remote cipher exists yet - the C's `takion->gkcrypt_remote`.</summary>
    bool CryptAvailable { get; }

    /// <summary>Whether any packet is being held back - the C's `takion->postponed_packets`.</summary>
    bool HasPostponed { get; }

    /// <summary>How long the next receive may wait, from the AV queues. Zero means already due.</summary>
    ulong NextTimeoutMs { get; }

    /// <summary>
    /// Re-check the MACs of everything already queued, dropping what fails.
    ///
    /// Injected rather than reproduced: its body is PP107's accepted pair - a peek handed NULL and a
    /// drop that leaves the element in the queue - and this line is about WHEN it runs, not what it
    /// does while it runs.
    /// </summary>
    void RecheckMacs();

    /// <summary>Hand on the postponed packets and let the array go.</summary>
    void FlushPostponed();

    /// <summary>Flush the AV queues on a timeout.</summary>
    void FlushWithTimeout();

    /// <summary>Wait for a datagram, into the buffer, for at most this long.</summary>
    TakionReceiveResult Receive(Span<byte> into, ulong timeoutMs);

    /// <summary>Hand a datagram to the handler.</summary>
    void Dispatch(Span<byte> datagram);
}

/// <summary>
/// PP487, under PP27: takion's receive thread - the order it does things in and the three ways one
/// iteration can end.
///
/// PP485 put a pooled buffer under this thread; this is the thread. It runs over
/// <see cref="TakionReceiveBuffer"/>, so the loop that was two heap operations per packet in the C
/// is none here, and the socket is behind <see cref="ITakionLoopHost"/> so none of it needs a
/// console.
///
/// TWO TRANSITIONS FIRE ONCE EACH AND THEIR GUARDS ARE NOT THE SAME. This is the whole reason the
/// line exists. `crypt_available` is read from `gkcrypt_remote` BEFORE the loop starts, so a session
/// that already holds the cipher never takes the first branch at all. When it does fire, the MAC
/// re-check is guarded by all three of `enable_crypt && !crypt_available && gkcrypt_remote`. The
/// postpone flush, a few lines below, tests `postponed_packets && gkcrypt_remote` and does NOT test
/// enable_crypt - so a session with crypt disabled still flushes what it postponed, and does not
/// re-check any MACs. Both run on the same iteration when the cipher appears, re-check first.
///
/// That asymmetry is the kind a reader tidies into symmetry on the way past, so
/// <see cref="TakionReceiveLoopSource"/> asserts both guards as the C spells them.
///
/// THREE EXITS FROM ONE ITERATION, TWO OF WHICH LOOK ALIKE. A next timeout of zero flushes the AV
/// queues and continues WITHOUT ATTEMPTING A RECEIVE - PP449 modelled the timeout itself, not the
/// branch that skips the socket on it. A receive that times out flushes and continues too, which
/// from outside is the same two actions in the same order and is a different path through the
/// function. Any other receive error leaves the loop, and that is how the thread ends.
/// </summary>
public static class TakionReceiveLoop
{
    /// <summary>
    /// Runs the loop until a receive fails.
    /// </summary>
    /// <param name="host">The world.</param>
    /// <param name="enableCrypt">The C's `takion->enable_crypt`, which only the first guard reads.</param>
    /// <param name="iterationLimit">
    /// A harness bound and NOT the C's behaviour: the C loops until a receive fails and has no count.
    /// A host that never fails would otherwise spin forever, so the outcome reports which of the two
    /// stopped it and a test can assert that it was the failure.
    /// </param>
    public static TakionLoopOutcome Run(ITakionLoopHost host, bool enableCrypt, int iterationLimit = 16)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterationLimit);

        var trace = new List<TakionLoopStep>();

        // Read once, before the loop, exactly where the C reads it: a session that already has the
        // cipher has nothing to transition to, so the re-check never runs for it.
        bool cryptAvailable = host.CryptAvailable;

        using var buffer = new TakionReceiveBuffer();

        int iterations = 0;
        while (true)
        {
            if (iterations >= iterationLimit)
                return new TakionLoopOutcome(trace, iterations, false, true);

            iterations++;

            // All three, and the flag is set whether or not the re-check finds anything.
            if (enableCrypt && !cryptAvailable && host.CryptAvailable)
            {
                cryptAvailable = true;
                host.RecheckMacs();
                trace.Add(TakionLoopStep.RecheckMacs);
            }

            // Two, and enable_crypt is not one of them.
            if (host.HasPostponed && host.CryptAvailable)
            {
                host.FlushPostponed();
                trace.Add(TakionLoopStep.FlushPostponed);
            }

            ulong timeout = host.NextTimeoutMs;
            if (timeout == 0)
            {
                host.FlushWithTimeout();
                trace.Add(TakionLoopStep.FlushOnZeroTimeout);
                continue;
            }

            trace.Add(TakionLoopStep.Receive);
            TakionReceiveResult result = host.Receive(buffer.Free, timeout);

            if (result.Outcome == TakionReceiveOutcome.Timeout)
            {
                host.FlushWithTimeout();
                trace.Add(TakionLoopStep.FlushOnTimeout);
                continue;
            }

            if (result.Outcome == TakionReceiveOutcome.Failed)
                return new TakionLoopOutcome(trace, iterations, true, false);

            buffer.Received(result.Length);
            host.Dispatch(buffer.Writable);
            trace.Add(TakionLoopStep.Dispatch);
        }
    }
}

/// <summary>
/// PP487: the C's own spelling of the two guards, so the asymmetry above is asserted and not read.
/// </summary>
public static class TakionReceiveLoopSource
{
    /// <summary>takion.c, through the one path constant this port already has twice.</summary>
    public static string? Locate() => TakionReceiveBuffer.LocateTakion();

    /// <summary>Whether crypt_available is still read from the cipher before the loop begins.</summary>
    public static bool CryptAvailableIsReadBeforeTheLoop(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains(
            "bool crypt_available = takion->gkcrypt_remote ? true : false;", StringComparison.Ordinal);
    }

    /// <summary>Whether the MAC re-check still tests all three, enable_crypt included.</summary>
    public static bool TheRecheckTestsEnableCrypt(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains(
            "if(takion->enable_crypt && !crypt_available && takion->gkcrypt_remote)",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the postpone flush still tests only two, enable_crypt excluded.
    ///
    /// The half that makes it an asymmetry rather than a pair. If a later edit adds enable_crypt
    /// here, the two become symmetric and the model above is wrong in a way nothing else would
    /// notice.
    /// </summary>
    public static bool ThePostponeFlushDoesNotTestEnableCrypt(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains(
            "if(takion->postponed_packets && takion->gkcrypt_remote)", StringComparison.Ordinal);
    }

    /// <summary>Whether a next timeout of zero still skips the receive rather than passing zero to it.</summary>
    public static bool AZeroTimeoutSkipsTheReceive(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("if(recv_timeout_ms == 0)", StringComparison.Ordinal);
    }

    /// <summary>Whether a timed-out receive still continues where any other error breaks.</summary>
    public static bool ATimeoutContinuesAndAnythingElseBreaks(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("if(err == CHIAKI_ERR_TIMEOUT)", StringComparison.Ordinal);
    }
}
