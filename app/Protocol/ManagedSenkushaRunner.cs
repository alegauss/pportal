using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What one senkusha handover produced, for a caller that is not the session thread.</summary>
/// <param name="Started">Whether the session thread reached the senkusha phase at all.</param>
/// <param name="Error">What the run answered, or Timeout where the start never came.</param>
/// <param name="Reading">The run's whole answer, or null where no run happened.</param>
public readonly record struct SenkushaRunnerOutcome(
    bool Started, ChiakiError Error, SenkushaRunReading? Reading);

/// <summary>
/// PP809, under PP784: the managed side of PP792's seam - wait, build, run, report.
///
/// <see cref="ManagedStreamRunner"/> is the model and the shape is deliberately the same, because
/// the two seams are the same seam twice: the C session thread blocks on a condition, this waits on
/// the other side of it, and what comes back decides what the thread does next.
///
/// WHAT COMES BACK IS FOUR VALUES AND NOT TWO. A stream run answers an error and a disconnect
/// reason; senkusha answers an error and three measurements, and PP28's placement spends them -
/// SUCCESS keeps them, CANCELED ends the session, and anything else falls back. So the whole
/// <see cref="SenkushaRunReading"/> crosses rather than the code alone.
///
/// A START THAT NEVER COMES IS NOT A RUN, which is <see cref="ManagedStreamRunner"/>'s rule and
/// matters more here: building the host opens a socket, and one made for a session that never
/// arrived would have to be torn down by whoever noticed.
/// </summary>
public sealed class ManagedSenkushaRunner
{
    private readonly Func<ManagedSenkushaRunHost> build;

    /// <summary>Takes how to build a host, which is the caller's composition root.</summary>
    public ManagedSenkushaRunner(Func<ManagedSenkushaRunHost> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        this.build = build;
    }

    /// <summary>
    /// How long to wait for the session thread to reach the senkusha phase.
    ///
    /// Shorter than the stream's thirty seconds because this step comes EARLIER: senkusha runs a
    /// few lines after ctrl reports a session id, and a session that has not arrived by now has
    /// failed somewhere this cannot see.
    /// </summary>
    public int StartTimeoutMs { get; init; } = 30000;

    /// <summary>The host this runner built, once it has built one.</summary>
    public ManagedSenkushaRunHost? Host { get; private set; }

    /// <summary>Waits for the handover, runs, and reports.</summary>
    /// <param name="handover">PP792's seam, whose far side is the C session thread.</param>
    public SenkushaRunnerOutcome Run(SenkushaHandover handover)
    {
        ArgumentNullException.ThrowIfNull(handover);

        if (!handover.AwaitStart(StartTimeoutMs))
        {
            handover.Finish(ChiakiError.Timeout, 0, 0, 0);
            return new SenkushaRunnerOutcome(false, ChiakiError.Timeout, null);
        }

        // PP768's read, one seam over: a cancelled wait ends the same way a start does, so without
        // this a phase going away would build a host and open a socket on its way out.
        if (handover.Stopped)
        {
            handover.Finish(ChiakiError.Canceled, 0, 0, 0);
            return new SenkushaRunnerOutcome(false, ChiakiError.Canceled, null);
        }

        Host = build();

        SenkushaRunReading reading = ManagedSenkushaRun.Run(Host);

        // Reported before returning, because the session thread is blocked on it: a runner that
        // answered its caller first would leave that thread waiting on a run already over.
        handover.Finish(reading);

        return new SenkushaRunnerOutcome(true, reading.Error, reading);
    }
}
