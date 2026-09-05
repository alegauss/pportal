namespace ChiakiNg.Protocol;

/// <summary>The two numbers one congestion packet carries, after the clamp has had them.</summary>
/// <param name="Received">What the console is told arrived.</param>
/// <param name="Lost">What the console is told did not.</param>
public readonly record struct CongestionReport(ushort Received, ushort Lost);

/// <summary>Where a report goes. The takion in the C; whatever the run gives it here.</summary>
public interface ICongestionSink
{
    /// <summary>Send one report upstream. Its failure is not the thread's business.</summary>
    void Send(CongestionReport report);
}

/// <summary>
/// PP714: congestioncontrol.c - the 200ms thread that tells the console what it lost.
///
/// Two of the seven members PP712 counted as owed by the run's host are this thread's start and its
/// stop, and it is the smallest of the four subsystems that were missing - a timer and a clamp,
/// against a feedback sender four times its size and a BIG message's protobuf.
///
/// WHAT IT SENDS IS NOT WHAT IT MEASURED. Where the measured loss ratio is above the configured
/// maximum the C does not clamp the RATIO and derive a number from it - it rewrites both numbers,
/// setting lost to a truncated fraction of the unchanged total and received to whatever is left.
/// The console is therefore told a smaller loss over the SAME total, which is a different pair from
/// the one a port that clamped the ratio would send. <see cref="Clamp"/> is that arithmetic on its
/// own, because it is the part worth asserting and the loop around it is a timer.
///
/// The default maximum is settings/packet_loss_max, whose 0.05 this side already declares.
/// </summary>
public sealed class ManagedCongestionControl : IDisposable
{
    /// <summary>CONGESTION_CONTROL_INTERVAL_MS. A report every fifth of a second.</summary>
    public const int IntervalMs = 200;

    private readonly ManagedPacketStats stats;
    private readonly ICongestionSink sink;
    private readonly double lossMax;
    private readonly ManualResetEventSlim stop = new(false);

    private Thread? thread;

    /// <param name="stats">Read with reset, so each report covers the interval since the last.</param>
    /// <param name="sink">Where a report goes.</param>
    /// <param name="lossMax">The reported ceiling, settings/packet_loss_max in the client.</param>
    public ManagedCongestionControl(ManagedPacketStats stats, ICongestionSink sink, double lossMax)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(sink);

        this.stats = stats;
        this.sink = sink;
        this.lossMax = lossMax;
    }

    /// <summary>The last ratio measured, before the clamp. The C keeps it on the struct too.</summary>
    public double PacketLoss { get; private set; }

    /// <summary>How many reports have gone out, which is what a test waits on.</summary>
    public int Reports { get; private set; }

    /// <summary>Whether the thread is up. StartCongestionControl, one of PP712's owed seven.</summary>
    public bool Running => thread is not null;

    /// <summary>
    /// The clamp, on its own.
    ///
    /// Both numbers are rewritten against an UNCHANGED total: lost becomes the truncated product of
    /// the total and the ceiling, and received becomes the remainder. So the console sees the same
    /// number of packets accounted for either way, with the loss among them reduced - which is the
    /// point, since a report is what its bitrate control reacts to.
    ///
    /// Truncation is C's, and it rounds the reported loss DOWN. The narrowing to sixteen bits is
    /// the C's too, and it is where a window of more than 65535 packets stops being reported
    /// honestly - a case this returns rather than guards, because guarding it would be the port
    /// disagreeing with what the console is told.
    /// </summary>
    public static CongestionReport Clamp(ulong received, ulong lost, double lossMax, out double measured)
    {
        ulong total = received + lost;
        measured = total > 0 ? (double)lost / total : 0;

        if (measured > lossMax)
        {
            lost = (ulong)(total * lossMax);
            received = total - lost;
        }

        return new CongestionReport((ushort)received, (ushort)lost);
    }

    /// <summary>What one tick would send, without a thread. The loop's body, made askable.</summary>
    public CongestionReport Tick()
    {
        PacketWindow window = stats.Read(true);
        CongestionReport report = Clamp(window.Received, window.Lost, lossMax, out double measured);

        PacketLoss = measured;
        Reports++;
        sink.Send(report);

        return report;
    }

    /// <summary>
    /// Start the thread. It reports every <see cref="IntervalMs"/> until stopped.
    ///
    /// The C waits on a condition with a bool predicate and treats anything but a TIMEOUT as the
    /// signal to leave, so the wait EXPIRING is the working case and being woken is the end. An
    /// event's Wait returns the two the other way round, which is the one place the shape differs.
    /// </summary>
    public void Start()
    {
        if (thread is not null)
            throw new InvalidOperationException("congestion control is already running.");

        thread = new Thread(Loop) { IsBackground = true, Name = "Chiaki Congestion Control" };
        thread.Start();
    }

    /// <summary>Signal and join, in that order. StopCongestionControl, the other owed member.</summary>
    public void Stop()
    {
        if (thread is null)
            return;

        stop.Set();
        thread.Join();
        thread = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
        stop.Dispose();
    }

    private void Loop()
    {
        while (!stop.Wait(IntervalMs))
            Tick();
    }
}
