using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP753: the seam PP696 cannot land without, exercised from both sides.
///
/// PP752 decided the C session thread waits for the managed run and takes back two values. The
/// shim's own rule rules out a managed function pointer, so the thread blocks on a condition and
/// this signals it. These stand where session.c will: one side starts and awaits the finish, the
/// other awaits the start and reports.
/// </summary>
public class StreamHandoverTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE FULL EXCHANGE, with the two sides on different threads.
    ///
    /// Which is the only arrangement that proves anything: a handover both halves of which run on
    /// one thread would pass whether or not the waits work.
    /// </summary>
    [Fact]
    public void TheThreadWaitsAndTheManagedSideReports()
    {
        using var handover = new StreamHandover();

        // Standing where session.c will: reach the stream phase, then block on the run's outcome.
        ChiakiError outcome = ChiakiError.Unknown;

        var sessionThread = new Thread(() =>
        {
            handover.Start();
            outcome = handover.AwaitFinish(5000);
        })
        {
            IsBackground = true,
            Name = "session thread",
        };

        sessionThread.Start();

        // And here is the managed side: it begins when the thread arrives rather than polling.
        Assert.True(handover.AwaitStart(5000), "the session thread never reached the stream phase");

        Assert.Equal(ChiakiError.Success, handover.Finish(ChiakiError.Disconnected, "the console hung up"));

        Assert.True(sessionThread.Join(TimeSpan.FromSeconds(10)));

        output.WriteLine($"the thread took back {outcome} and \"{handover.Reason}\"");

        Assert.Equal(ChiakiError.Disconnected, outcome);
        Assert.Equal("the console hung up", handover.Reason);
    }

    /// <summary>
    /// A FINISH THAT NEVER COMES IS A TIMEOUT, and not the error the handover was built with.
    ///
    /// The session thread has to tell a run that failed from one that never reported: the first is
    /// a quit reason it writes, the second is a bug it should not disguise as one.
    /// </summary>
    [Fact]
    public void AWaitThatRunsOutAnswersTimeout()
    {
        using var handover = new StreamHandover();

        var clock = Stopwatch.StartNew();
        ChiakiError outcome = handover.AwaitFinish(60);
        clock.Stop();

        output.WriteLine($"waited {clock.ElapsedMilliseconds}ms for a finish that never came");

        Assert.Equal(ChiakiError.Timeout, outcome);
        Assert.Null(handover.Reason);
    }

    /// <summary>And a start that never comes is false rather than a hang.</summary>
    [Fact]
    public void AStartThatNeverComesIsFalse()
    {
        using var handover = new StreamHandover();

        Assert.False(handover.AwaitStart(60));
    }

    /// <summary>
    /// A finish already reported is taken without waiting, which is the race the flag exists for.
    ///
    /// The managed run can return before the session thread reaches its wait. A handover that only
    /// signalled would lose that, and the thread would block for its whole timeout on an outcome
    /// that had already arrived.
    /// </summary>
    [Fact]
    public void AFinishThatArrivedFirstIsStillTaken()
    {
        using var handover = new StreamHandover();

        Assert.Equal(ChiakiError.Success, handover.Finish(ChiakiError.Success, reason: null));

        var clock = Stopwatch.StartNew();
        ChiakiError outcome = handover.AwaitFinish(5000);
        clock.Stop();

        output.WriteLine($"took {outcome} after {clock.ElapsedMilliseconds}ms");

        Assert.Equal(ChiakiError.Success, outcome);
        Assert.True(clock.ElapsedMilliseconds < 1000, "the wait blocked on an outcome it already had");
    }

    /// <summary>The same for the start, which the managed side may reach late.</summary>
    [Fact]
    public void AStartThatArrivedFirstIsStillSeen()
    {
        using var handover = new StreamHandover();

        handover.Start();

        Assert.True(handover.AwaitStart(0));
    }

    /// <summary>
    /// A NULL REASON STAYS NULL, which PP371 is the reason for.
    ///
    /// The C reads the reason twice on the disconnect path and both reads dereference it. An empty
    /// string standing in for an absent one would make a console that gave no reason look like one
    /// that gave a blank, and the two choose different quit reasons.
    /// </summary>
    [Fact]
    public void AnAbsentReasonIsNotAnEmptyOne()
    {
        using var handover = new StreamHandover();

        handover.Finish(ChiakiError.Disconnected, reason: null);
        Assert.Null(handover.Reason);

        using var other = new StreamHandover();

        other.Finish(ChiakiError.Disconnected, reason: string.Empty);
        Assert.Equal(string.Empty, other.Reason);
    }

    /// <summary>The last report wins, and the reason it replaces is released rather than leaked.</summary>
    [Fact]
    public void ASecondReportReplacesTheFirst()
    {
        using var handover = new StreamHandover();

        handover.Finish(ChiakiError.Unknown, "first");
        handover.Finish(ChiakiError.Success, "second");

        Assert.Equal("second", handover.Reason);
        Assert.Equal(ChiakiError.Success, handover.AwaitFinish(0));
    }

    /// <summary>Disposing closes the seam, and using it afterwards says so rather than crashing.</summary>
    [Fact]
    public void AClosedHandoverRefusesRatherThanCrashes()
    {
        var handover = new StreamHandover();
        Assert.True(handover.IsOpen);

        handover.Dispose();
        Assert.False(handover.IsOpen);

        Assert.Throws<ObjectDisposedException>(() => handover.Start());
        Assert.Throws<ObjectDisposedException>(() => handover.AwaitFinish(0));

        // And a second dispose is a no-op rather than a double free.
        handover.Dispose();
    }
}
