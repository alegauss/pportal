using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP487, under PP27: the receive thread's order, and the three ways one iteration ends.
///
/// The socket is injected, so none of this needs a console. What it is for is the two once-only
/// transitions at the top of the loop, whose guards differ by one term - and the two exits that do
/// the same two things in the same order down different paths.
/// </summary>
public class TakionReceiveLoopTests
{
    /// <summary>A host the test scripts, standing in for the socket and the AV queues.</summary>
    private sealed class ScriptedHost : ITakionLoopHost
    {
        public bool CryptAvailable { get; set; }

        public bool HasPostponed { get; set; }

        public ulong NextTimeoutMs { get; set; } = 100;

        public int Rechecks { get; private set; }

        public int PostponeFlushes { get; private set; }

        public int TimeoutFlushes { get; private set; }

        public ulong LastTimeout { get; private set; }

        public List<byte[]> Dispatched { get; } = [];

        public Queue<TakionReceiveResult> Receives { get; } = new();

        /// <summary>Run before each receive returns, so a test can make the cipher appear mid-loop.</summary>
        public Action<ScriptedHost>? OnReceive { get; set; }

        public void RecheckMacs() => Rechecks++;

        // The C frees the array and nulls the pointer, so the guard cannot be true twice.
        public void FlushPostponed()
        {
            PostponeFlushes++;
            HasPostponed = false;
        }

        public void FlushWithTimeout() => TimeoutFlushes++;

        public TakionReceiveResult Receive(Span<byte> into, ulong timeoutMs)
        {
            LastTimeout = timeoutMs;
            OnReceive?.Invoke(this);

            TakionReceiveResult result = Receives.Count > 0
                ? Receives.Dequeue()
                : new TakionReceiveResult(TakionReceiveOutcome.Failed, 0);

            if (result.Outcome == TakionReceiveOutcome.Datagram)
            {
                for (int i = 0; i < result.Length; i++)
                    into[i] = (byte)(i + 1);
            }

            return result;
        }

        public void Dispatch(ReadOnlySpan<byte> datagram) => Dispatched.Add(datagram.ToArray());
    }

    /// <summary>A failed receive is the loop's only exit, which is how the thread ends.</summary>
    [Fact]
    public void TheThreadEndsOnAFailedReceive()
    {
        var host = new ScriptedHost();
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.True(outcome.ExitedOnFailure);
        Assert.False(outcome.HitLimit);
        Assert.Equal(1, outcome.Iterations);
        Assert.Equal([TakionLoopStep.Receive], outcome.Trace);
    }

    /// <summary>A datagram is handed on at the length that arrived, not at the buffer's size.</summary>
    [Fact]
    public void ADatagramIsDispatchedAtTheLengthThatArrived()
    {
        var host = new ScriptedHost();
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Datagram, 4));
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true);

        byte[] dispatched = Assert.Single(host.Dispatched);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, dispatched);
        Assert.Equal(
            [TakionLoopStep.Receive, TakionLoopStep.Dispatch, TakionLoopStep.Receive],
            outcome.Trace);
    }

    /// <summary>
    /// THE ASYMMETRY: crypt appearing flushes the postponed packets even with crypt DISABLED, and
    /// does not re-check any MACs.
    ///
    /// The re-check tests `enable_crypt && !crypt_available && gkcrypt_remote`; the postpone flush, a
    /// few lines below, tests `postponed_packets && gkcrypt_remote` and leaves enable_crypt out. So a
    /// session that postponed packets without the cipher enabled still hands them on when one turns
    /// up. Tidying the two into symmetry is the mistake this pins.
    /// </summary>
    [Fact]
    public void CryptAppearingFlushesThePostponedEvenWithCryptDisabled()
    {
        var host = new ScriptedHost { CryptAvailable = false, HasPostponed = true };
        host.OnReceive = h => h.CryptAvailable = true;
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Datagram, 1));
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: false);

        Assert.Equal(1, host.PostponeFlushes);
        Assert.Equal(0, host.Rechecks);
        Assert.Contains(TakionLoopStep.FlushPostponed, outcome.Trace);
        Assert.DoesNotContain(TakionLoopStep.RecheckMacs, outcome.Trace);
    }

    /// <summary>And with crypt enabled both fire, on the same iteration, re-check first.</summary>
    [Fact]
    public void WithCryptEnabledBothFireAndTheRecheckIsFirst()
    {
        var host = new ScriptedHost { CryptAvailable = false, HasPostponed = true };
        host.OnReceive = h => h.CryptAvailable = true;
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Datagram, 1));
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.Equal(1, host.Rechecks);
        Assert.Equal(1, host.PostponeFlushes);

        int recheck = outcome.Trace.ToList().IndexOf(TakionLoopStep.RecheckMacs);
        int flush = outcome.Trace.ToList().IndexOf(TakionLoopStep.FlushPostponed);
        Assert.True(recheck >= 0 && flush > recheck, string.Join(", ", outcome.Trace));
    }

    /// <summary>
    /// A session that already holds the cipher never re-checks, because the flag is read before the
    /// loop and there is no transition left to see.
    /// </summary>
    [Fact]
    public void ASessionThatAlreadyHasTheCipherNeverRechecks()
    {
        var host = new ScriptedHost { CryptAvailable = true };
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.Equal(0, host.Rechecks);
        Assert.DoesNotContain(TakionLoopStep.RecheckMacs, outcome.Trace);
    }

    /// <summary>The re-check happens once however many iterations follow it.</summary>
    [Fact]
    public void TheRecheckHappensOnce()
    {
        var host = new ScriptedHost { CryptAvailable = false };
        host.OnReceive = h => h.CryptAvailable = true;
        for (int i = 0; i < 4; i++)
            host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Datagram, 2));
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.Equal(1, host.Rechecks);
    }

    /// <summary>
    /// A next timeout of zero flushes and goes round WITHOUT attempting a receive.
    ///
    /// The queue of receives is left untouched, which is the assertion: the C skips the socket on
    /// this branch rather than calling it with a zero timeout.
    /// </summary>
    [Fact]
    public void AZeroTimeoutFlushesWithoutTouchingTheSocket()
    {
        var host = new ScriptedHost { NextTimeoutMs = 0 };
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Datagram, 8));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true, iterationLimit: 3);

        Assert.True(outcome.HitLimit);
        Assert.False(outcome.ExitedOnFailure);
        Assert.Equal(3, host.TimeoutFlushes);
        Assert.Single(host.Receives);
        Assert.Empty(host.Dispatched);
        Assert.DoesNotContain(TakionLoopStep.Receive, outcome.Trace);
    }

    /// <summary>
    /// And a receive that times out flushes and continues, which is the same two actions down a
    /// different path - so the trace distinguishes them where the behaviour does not.
    /// </summary>
    [Fact]
    public void ATimedOutReceiveFlushesAndContinues()
    {
        var host = new ScriptedHost();
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Timeout, 0));
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionLoopOutcome outcome = TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.Equal(1, host.TimeoutFlushes);
        Assert.True(outcome.ExitedOnFailure);
        Assert.Equal(
            [TakionLoopStep.Receive, TakionLoopStep.FlushOnTimeout, TakionLoopStep.Receive],
            outcome.Trace);
        Assert.DoesNotContain(TakionLoopStep.FlushOnZeroTimeout, outcome.Trace);
    }

    /// <summary>The timeout handed to the socket is the AV queues' own.</summary>
    [Fact]
    public void TheTimeoutTheSocketGetsIsTheQueuesOwn()
    {
        var host = new ScriptedHost { NextTimeoutMs = 250 };
        host.Receives.Enqueue(new TakionReceiveResult(TakionReceiveOutcome.Failed, 0));

        TakionReceiveLoop.Run(host, enableCrypt: true);

        Assert.Equal(250UL, host.LastTimeout);
    }

    /// <summary>A limit of zero or less is refused rather than run once.</summary>
    [Fact]
    public void AnEmptyLimitIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => TakionReceiveLoop.Run(new ScriptedHost(), true, 0));

    /// <summary>
    /// THE DRIFT CHECK: the C still spells both guards the way the asymmetry above requires.
    ///
    /// If a later edit adds enable_crypt to the postpone flush, the two become symmetric and the
    /// model is wrong in a way no managed test would otherwise notice.
    /// </summary>
    [Fact]
    public void TheCStillSpellsTheTwoGuardsDifferently()
    {
        if (TakionReceiveLoopSource.Locate() is not { } path)
            return;

        string text = File.ReadAllText(path);

        Assert.True(TakionReceiveLoopSource.CryptAvailableIsReadBeforeTheLoop(text));
        Assert.True(TakionReceiveLoopSource.TheRecheckTestsEnableCrypt(text));
        Assert.True(TakionReceiveLoopSource.ThePostponeFlushDoesNotTestEnableCrypt(text));
        Assert.True(TakionReceiveLoopSource.AZeroTimeoutSkipsTheReceive(text));
        Assert.True(TakionReceiveLoopSource.ATimeoutContinuesAndAnythingElseBreaks(text));
    }
}
