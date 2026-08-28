namespace ChiakiNg.Protocol;

/// <summary>What replaying a capture through the managed path found.</summary>
/// <param name="Counters">The path's own counters, summed over the capture.</param>
/// <param name="AllocatedBytes">What the replay allocated after its warm-up. Expected zero.</param>
/// <param name="SpanMicroseconds">Arrival span of the capture, first to last.</param>
/// <param name="Replayed">How many datagrams were run.</param>
public readonly record struct ReplayReport(
    TakionReceiveCounters Counters, long AllocatedBytes, long SpanMicroseconds, int Replayed);

/// <summary>
/// PP513, under PP27: running a captured file back through the managed receive path.
///
/// PP510 settled the format, PP511 made the C fill it, PP512 got it onto disk. Nothing read one
/// back and ran it, so the file a session leaves would sit there with no number beside it.
///
/// PP27 ASKED FOR THIS IN ITS OWN WORDS: the oracle "can run both against the same captured traffic
/// and compare timing, not just bytes". This is the managed half - the half a checkout can build.
///
/// WHAT A REPLAY REPORTS IS NOT A TIME. A number measured inside a test is about the machine that
/// ran it, so this reports what does not move: which branch each datagram took, how many bytes the
/// branch had to copy, and how much was allocated. A timing run is this with a stopwatch around it,
/// and belongs to the session rather than to the suite.
///
/// THE ALLOCATION CLAIM IS MADE AGAIN, ON DIFFERENT INPUT. PP500 held the composed path at zero
/// bytes over datagrams a test invented. A replayed capture is datagrams a console sent, and a path
/// that allocated only on shapes synthetic input never produced would have passed there and fail
/// here.
///
/// WHAT THE SESSION ADDS IS THE OTHER SIDE. Running the C over the same file needs a takion the
/// shim cannot hand out - PP481's subject - so this reports the managed side and says so, rather
/// than a comparison with one half missing.
/// </summary>
public static class TakionCaptureReplay
{
    /// <summary>How many datagrams are run before the allocation window opens.</summary>
    /// <remarks>
    /// The same shape as PP500's and as test/allocbudget.c's: first-call costs are outside the
    /// window, because they are paid once and are not what a per-packet budget is about.
    /// </remarks>
    public const int WarmUp = 8;

    /// <summary>
    /// Runs a capture through <see cref="TakionReceivePath"/>.
    /// </summary>
    /// <param name="datagrams">A capture, as <see cref="TakionCaptureFile.Read"/> returns one.</param>
    /// <param name="sink">Where a branch's work goes. Must not allocate, or the report is its.</param>
    /// <param name="macOk">
    /// Whether the MAC gate passed. A capture cannot say - PP511 emits above the gate - so this is
    /// the replay's assumption and is stated rather than guessed per datagram.
    /// </param>
    public static ReplayReport Run(
        IReadOnlyList<CapturedDatagram> datagrams, ITakionSink sink, bool macOk = true)
    {
        ArgumentNullException.ThrowIfNull(datagrams);
        ArgumentNullException.ThrowIfNull(sink);

        var counters = default(TakionReceiveCounters);

        int warmUp = Math.Min(WarmUp, datagrams.Count);
        for (var i = 0; i < warmUp; i++)
            Feed(datagrams[i], sink, ref counters);

        // Everything before this line is paid once. Everything after is per packet.
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = warmUp; i < datagrams.Count; i++)
            Feed(datagrams[i], sink, ref counters);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        long span = datagrams.Count > 0
            ? datagrams[^1].ArrivalMicroseconds - datagrams[0].ArrivalMicroseconds
            : 0;

        return new ReplayReport(counters, allocated, span, datagrams.Count);

        void Feed(CapturedDatagram datagram, ITakionSink into, ref TakionReceiveCounters running)
            => TakionReceivePath.Handle(
                datagram.Head, into, ref running, macOk, enableCrypt: true, cryptAvailable: true);
    }

    /// <summary>
    /// The mean gap between arrivals, in microseconds, or null for a capture too short to have one.
    ///
    /// Null and not zero: one datagram has no spacing, and a zero there would be a measurement
    /// nobody took. The same rule <see cref="TakionTimingCapture.InterArrivalMicroseconds"/> keeps.
    /// </summary>
    public static double? MeanGapMicroseconds(IReadOnlyList<CapturedDatagram> datagrams)
    {
        ArgumentNullException.ThrowIfNull(datagrams);

        if (datagrams.Count < 2)
            return null;

        long span = datagrams[^1].ArrivalMicroseconds - datagrams[0].ArrivalMicroseconds;
        return (double)span / (datagrams.Count - 1);
    }
}

/// <summary>
/// A sink that counts and copies into a buffer it already owns, so a replay's report is the path's.
///
/// Public for the reason PP508's recording session is: the instrument belongs on the same side of
/// the seam as the thing it measures. A sink that allocated would put its own bytes into a report
/// about the receive path.
/// </summary>
public sealed class CountingReplaySink : ITakionSink
{
    private readonly byte[] kept = new byte[TakionReceiveBuffer.DatagramCapacity];

    /// <summary>How many datagrams a branch kept.</summary>
    public int Kept { get; private set; }

    /// <summary>And how many it borrowed.</summary>
    public int Borrowed { get; private set; }

    /// <summary></summary>
    public void Keep(TakionDispatchBranch branch, ReadOnlySpan<byte> datagram)
    {
        datagram.CopyTo(kept);
        Kept++;
    }

    /// <summary></summary>
    public void Borrow(TakionDispatchBranch branch, ReadOnlySpan<byte> datagram) => Borrowed++;
}
