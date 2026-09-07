using System.Diagnostics;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP792, under PP784: senkusha's own seam, exercised from both sides.
///
/// PP753's stream handover is the model and the cargo is what differs: a senkusha run answers three
/// measurements as well as an error, and PP28's placement is what spends them. So these stand where
/// session.c stands - one side starts and blocks on the outcome, the other awaits the start and
/// reports - and the assertions are about the numbers surviving the crossing.
/// </summary>
public class SenkushaHandoverTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE FULL EXCHANGE, with the two sides on different threads.
    ///
    /// The only arrangement that proves anything: a handover both halves of which run on one thread
    /// would pass whether or not the waits work.
    /// </summary>
    [Fact]
    public void TheThreadWaitsAndTheManagedSideReportsWhatItMeasured()
    {
        using var handover = new SenkushaHandover();

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

        Assert.True(handover.AwaitStart(5000), "the session thread never reached the senkusha phase");

        Assert.Equal(
            ChiakiError.Success,
            handover.Finish(ChiakiError.Success, mtuIn: 1454, mtuOut: 1420, roundTripMicroseconds: 8421));

        Assert.True(sessionThread.Join(TimeSpan.FromSeconds(10)));

        output.WriteLine($"the thread took back {outcome} and {handover.Measurements}");

        Assert.Equal(ChiakiError.Success, outcome);
        Assert.Equal(new SenkushaMeasurementsReading(1454, 1420, 8421), handover.Measurements);
    }

    /// <summary>
    /// THE TWO MTUs ARE NOT THE SAME NUMBER, and a seam that carried one would pass everything else.
    ///
    /// senkusha measures the two directions apart and PP777 found the launch spec wants mtu_in
    /// specifically, so a handover that collapsed them would send a console a size measured the
    /// other way - and every wait after that one still succeeds.
    /// </summary>
    [Fact]
    public void TheInboundAndOutboundMtusCrossSeparately()
    {
        using var handover = new SenkushaHandover();

        handover.Finish(ChiakiError.Success, mtuIn: 1454, mtuOut: 576, roundTripMicroseconds: 1);

        SenkushaMeasurementsReading? measured = handover.Measurements;

        Assert.NotNull(measured);
        Assert.Equal(1454u, measured.Value.MtuIn);
        Assert.Equal(576u, measured.Value.MtuOut);
    }

    /// <summary>
    /// Nothing measured is NULL rather than three zeroes.
    ///
    /// The session thread tells the two apart by the error, and a reader of the handover has no
    /// error to read - so a link nobody measured and a link measured as zeroes would be the same
    /// answer, which is the ambiguity PP371 found at the other seam's reason string.
    /// </summary>
    [Fact]
    public void NothingIsMeasuredUntilAFinishReports()
    {
        using var handover = new SenkushaHandover();

        Assert.Null(handover.Measurements);

        handover.Finish(ChiakiError.Unknown, 0, 0, 0);

        Assert.Equal(new SenkushaMeasurementsReading(0, 0, 0), handover.Measurements);
    }

    /// <summary>
    /// A FINISH THAT NEVER COMES IS A TIMEOUT, and not the code the handover was built with.
    ///
    /// PP28 sends both to the fallback, so the session survives either way - but a run that failed
    /// and a run that never reported are different defects and only one of them is on the far side.
    /// </summary>
    [Fact]
    public void AWaitThatRunsOutAnswersTimeout()
    {
        using var handover = new SenkushaHandover();

        var clock = Stopwatch.StartNew();
        ChiakiError outcome = handover.AwaitFinish(60);
        clock.Stop();

        output.WriteLine($"waited {clock.ElapsedMilliseconds}ms for a finish that never came");

        Assert.Equal(ChiakiError.Timeout, outcome);
        Assert.Null(handover.Measurements);
    }

    /// <summary>And a start that never comes is false rather than a hang.</summary>
    [Fact]
    public void AStartThatNeverComesIsFalse()
    {
        using var handover = new SenkushaHandover();

        Assert.False(handover.AwaitStart(60));
    }

    /// <summary>
    /// A finish already reported is taken without waiting, which is the race the flag exists for.
    ///
    /// The managed side can report before the session thread reaches its wait - senkusha is the
    /// step the thread runs immediately after ctrl, so the two are close together - and a wait that
    /// only ever returned on a signal would miss it and time out on a measurement that succeeded.
    /// </summary>
    [Fact]
    public void AFinishThatArrivedFirstIsStillTaken()
    {
        using var handover = new SenkushaHandover();

        handover.Finish(ChiakiError.Success, 1454, 1454, 999);

        var clock = Stopwatch.StartNew();
        ChiakiError outcome = handover.AwaitFinish(5000);
        clock.Stop();

        Assert.Equal(ChiakiError.Success, outcome);
        Assert.True(clock.ElapsedMilliseconds < 1000, $"it waited {clock.ElapsedMilliseconds}ms for a finish already in");
    }

    /// <summary>
    /// PP768's way out, one seam over: cancel releases BOTH waits and marks the handover stopped.
    ///
    /// Without it a caller holding a thread inside <see cref="SenkushaHandover.AwaitStart"/> could
    /// only free the object that thread was blocked on, and a wait on freed memory fails
    /// intermittently rather than reliably - which is the expensive way to find it.
    /// </summary>
    [Fact]
    public void CancelEndsBothWaitsAndSaysSo()
    {
        using var handover = new SenkushaHandover();

        Assert.False(handover.Stopped);

        var waiter = new Thread(() => Assert.True(handover.AwaitStart(5000)))
        {
            IsBackground = true,
            Name = "senkusha waiter",
        };

        waiter.Start();

        Assert.Equal(ChiakiError.Success, handover.Cancel());
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)), "the start wait was not released");
        Assert.True(handover.Stopped);

        // And the other wait, so a run that had already begun is released too.
        Assert.NotEqual(ChiakiError.Timeout, handover.AwaitFinish(5000));
    }

    /// <summary>
    /// No socket handed over is null and never handle zero.
    ///
    /// PP769's finding at the stream's seam, which applies unchanged: zero is a handle a caller
    /// could believe in, so the absence has to be a value no socket has.
    /// </summary>
    [Fact]
    public void NoSocketIsNullRatherThanZero()
    {
        using var handover = new SenkushaHandover();

        Assert.Null(handover.Socket);
    }

    /// <summary>
    /// A run's own reading crosses whole, which is the overload the far side actually calls.
    ///
    /// <see cref="ManagedSenkushaRun.Run"/> answers a <see cref="SenkushaRunReading"/> and PP790
    /// wrote it as the run's whole output, so the seam takes one rather than making every caller
    /// unpack the same four fields - and unpacking them in three places is how one gets swapped.
    /// </summary>
    [Fact]
    public void ARunsReadingCrossesWithoutBeingUnpackedByTheCaller()
    {
        using var handover = new SenkushaHandover();

        var reading = new SenkushaRunReading(
            ChiakiError.Success,
            SenkushaExit.Disconnected,
            SenkushaRung.MtuOutMeasured,
            RoundTripMicroseconds: 12345,
            MtuIn: 1454,
            MtuOut: 1400);

        Assert.Equal(ChiakiError.Success, handover.Finish(reading));
        Assert.Equal(new SenkushaMeasurementsReading(1454, 1400, 12345), handover.Measurements);
    }

    /// <summary>
    /// A disposed handover throws rather than calling into freed memory.
    ///
    /// Every reader takes the same guard, so this asks one of each kind: a wait, a property and the
    /// install - the three shapes a use-after-free would arrive through.
    /// </summary>
    [Fact]
    public void AClosedSeamRefusesEveryCall()
    {
        var handover = new SenkushaHandover();

        Assert.True(handover.IsOpen);
        handover.Dispose();
        Assert.False(handover.IsOpen);

        Assert.Throws<ObjectDisposedException>(() => { _ = handover.AwaitFinish(0); });
        Assert.Throws<ObjectDisposedException>(() => { _ = handover.Stopped; });
        Assert.Throws<ObjectDisposedException>(() => { _ = handover.Cancel(); });
    }
}
