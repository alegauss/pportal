using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What one handover produced, for a caller that is not the session thread.</summary>
/// <param name="Started">Whether the session thread reached the stream phase at all.</param>
/// <param name="Error">What the run answered, or Timeout where the start never came.</param>
/// <param name="Reason">The remote disconnect reason, or null where there was none.</param>
public readonly record struct StreamRunnerOutcome(bool Started, ChiakiError Error, string? Reason);

/// <summary>
/// PP754, under PP696: the managed side of PP753's seam - wait, build, run, report.
///
/// PP745 wrote the host, PP746 drove it over a socket and PP753 built the seam. Every construction
/// of the host and every call of the run was in the test project, so a session.c that handed over
/// would have waited its whole timeout and answered TIMEOUT. This is what answers.
///
/// IT TAKES ITS PARTS RATHER THAN FINDING THEM, for the reason the host does. The keys come from a
/// bang the managed side does not yet perform in a live session, so a runner that derived them
/// would be deciding where the bang happens as a side effect of deciding where the run does. The
/// composition root stays the caller's; what this owns is the sequence.
///
/// AND THE REPORT IS TWO VALUES. The session thread writes its quit reason from the error and the
/// remote disconnect reason together - PP371 found both of its reads dereferencing the second - so
/// a runner answering only the code would leave the reason for somebody else to remember.
///
/// A START THAT NEVER COMES IS NOT A RUN. The seam answers false on timeout, and this reports that
/// rather than building a host for a session that is not there: constructing one takes a socket.
/// </summary>
public sealed class ManagedStreamRunner
{
    private readonly Func<ManagedStreamRunHost> build;

    /// <summary>Takes how to build a host, which is the caller's composition root.</summary>
    public ManagedStreamRunner(Func<ManagedStreamRunHost> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        this.build = build;
    }

    /// <summary>How long to wait for the session thread to reach the stream phase.</summary>
    public int StartTimeoutMs { get; init; } = 30000;

    /// <summary>The host this runner built, once it has built one.</summary>
    public ManagedStreamRunHost? Host { get; private set; }

    /// <summary>
    /// Waits for the handover, runs, and reports - which is the whole of what this owns.
    /// </summary>
    /// <param name="handover">PP753's seam, whose far side is the C session thread.</param>
    public StreamRunnerOutcome Run(StreamHandover handover)
    {
        ArgumentNullException.ThrowIfNull(handover);

        if (!handover.AwaitStart(StartTimeoutMs))
        {
            // Nothing is built: a host owns a socket, and one made for a session that never
            // arrived would have to be torn down by whoever noticed.
            handover.Finish(ChiakiError.Timeout);
            return new StreamRunnerOutcome(false, ChiakiError.Timeout, null);
        }

        // PP768: A CANCELLED WAIT IS NOT A START. The seam had no way out of AwaitStart, so a caller
        // shutting down could only free the object this thread was blocked on. Cancel ends the wait
        // and sets the flag together, and this is the read that keeps that from looking like a
        // session arriving - the same build, the same socket, for a phase that is going away.
        if (handover.Stopped)
        {
            handover.Finish(ChiakiError.Canceled);
            return new StreamRunnerOutcome(false, ChiakiError.Canceled, null);
        }

        Host = build();

        ChiakiError error = ManagedStreamRun.Run(Host);

        // PP755: read off the host rather than handed in. The disconnect handler writes it there,
        // which is where the C keeps it, so a caller cannot report a reason the run never had.
        string? reason = Host.RemoteDisconnectReason;

        // Reported before returning, because the session thread is blocked on it: a runner that
        // answered its caller first would leave that thread waiting on a run already over.
        handover.Finish(error, reason);

        return new StreamRunnerOutcome(true, error, reason);
    }
}
