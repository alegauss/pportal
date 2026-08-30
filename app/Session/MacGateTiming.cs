using System.Diagnostics;
using System.Globalization;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>One side's cost, as microseconds per datagram over a run of batches.</summary>
/// <param name="Name">Which side.</param>
/// <param name="Batches">How many batch means the distribution is over.</param>
/// <param name="MinUs">The fastest batch mean.</param>
/// <param name="MeanUs">The mean of the batch means.</param>
/// <param name="P50Us">Median batch mean.</param>
/// <param name="P99Us">The 99th percentile batch mean, by nearest rank.</param>
/// <param name="MaxUs">The slowest batch mean.</param>
public readonly record struct MacGateCost(
    string Name, int Batches, double MinUs, double MeanUs, double P50Us, double P99Us, double MaxUs);

/// <summary>What one timed comparison found.</summary>
/// <param name="Datagrams">How many heads each batch ran over.</param>
/// <param name="Copy">The copy every side pays, timed alone so it can be subtracted.</param>
/// <param name="Managed">The managed model, copy included.</param>
/// <param name="Native">The C through the shim, copy included.</param>
public readonly record struct MacGateComparison(
    int Datagrams, MacGateCost Copy, MacGateCost Managed, MacGateCost Native);

/// <summary>
/// PP27's timing half, over the one piece of takion both implementations can be handed the same
/// real bytes.
///
/// §PP27 asks for it in those words - "the oracle can run both against the same captured traffic
/// and compare timing, not just bytes" - and PP517 already runs the halves for agreement. This
/// times them. The MAC gate is the right piece and not a convenient one: it is the per-packet hot
/// path, every datagram passes through it, and §PP27's whole worry is that thousands of small
/// packets a second are thousands of allocations if written carelessly.
///
/// WHAT THIS IS NOT is a comparison of the two receive paths. The shim exposes the gate, the send
/// buffer and the message codec; no entry point runs takion's receive loop, which is bound to
/// sockets and threads a capture has neither of. So this settles the gate and says nothing about
/// the loop around it.
///
/// Three instrument decisions, each of which the first draft would have got wrong:
///
/// BATCHES, NOT PACKETS. A Stopwatch read costs on the order of the work being timed here, so a
/// per-datagram stamp would be measuring the clock. Each sample is one pass over the whole capture
/// divided by its count, and the distribution is over passes - which is why it has tens of samples
/// and not tens of thousands. Same convention the present-path and video-upscale spikes settled on.
///
/// THE COPY IS TIMED ALONE. Both sides mutate the head they are given, so each pass has to hand
/// each side a fresh one. That copy is charged to both equally, which keeps the DIFFERENCE honest
/// and makes each absolute number too big. So it is measured on its own and reported beside them,
/// and a reader who wants the gate alone subtracts it rather than being asked to trust that it is
/// small.
///
/// THE VERDICT IS NOT RE-DERIVED HERE. Whether the two agree is PP517's question and
/// DatagramReplayReport.MacDisagreements answers it. A timing harness that also decided agreement
/// would be free to report both from one pass and quietly drop the copies, which is the shortcut
/// that makes a comparison meaningless.
/// </summary>
public static class MacGateTiming
{
    /// <summary>Passes over the capture that are thrown away before any is kept.</summary>
    public const int DefaultWarmup = 3;

    /// <summary>Passes kept, which is how many samples the distribution is over.</summary>
    public const int DefaultBatches = 20;

    /// <summary>Time both sides and the copy they share.</summary>
    public static MacGateComparison Measure(
        IReadOnlyList<CapturedDatagram> datagrams, int batches = DefaultBatches, int warmup = DefaultWarmup)
    {
        ArgumentNullException.ThrowIfNull(datagrams);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batches);
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);

        return new MacGateComparison(
            datagrams.Count,
            Time("copy", datagrams, batches, warmup, CopyOnly),
            Time("managed", datagrams, batches, warmup, ManagedGate),
            Time("c", datagrams, batches, warmup, NativeGate));
    }

    /// <summary>The copy alone, which is the floor under both of the others.</summary>
    private static void CopyOnly(CapturedDatagram datagram)
    {
        byte[] head = [.. datagram.Head];
        // Read back so a copy with no reader cannot be elided. One byte is enough to make the
        // array observable, and it is the cheapest observation there is.
        GC.KeepAlive(head.Length == 0 ? (byte)0 : head[0]);
    }

    private static void ManagedGate(CapturedDatagram datagram)
    {
        byte[] head = [.. datagram.Head];
        TakionPacketMac.MacResult result = TakionPacketMac.Apply(head, gmac: null);
        GC.KeepAlive(result);
    }

    private static void NativeGate(CapturedDatagram datagram)
    {
        byte[] head = [.. datagram.Head];
        ChiakiNg.Native.ChiakiError error = Takion.PacketMacWithoutCrypt(head, keyPos: 0, out byte[]? before);
        GC.KeepAlive(before);
        GC.KeepAlive(error);
    }

    private static MacGateCost Time(
        string name, IReadOnlyList<CapturedDatagram> datagrams, int batches, int warmup,
        Action<CapturedDatagram> one)
    {
        for (int w = 0; w < warmup; w++)
            Pass(datagrams, one);

        var samples = new double[batches];
        for (int b = 0; b < batches; b++)
        {
            long start = Stopwatch.GetTimestamp();
            Pass(datagrams, one);
            long elapsed = Stopwatch.GetTimestamp() - start;
            double us = elapsed * 1_000_000.0 / Stopwatch.Frequency;
            samples[b] = datagrams.Count == 0 ? 0 : us / datagrams.Count;
        }

        return Summarise(name, samples);
    }

    private static void Pass(IReadOnlyList<CapturedDatagram> datagrams, Action<CapturedDatagram> one)
    {
        foreach (CapturedDatagram datagram in datagrams)
            one(datagram);
    }

    /// <summary>
    /// Min, mean and two percentiles by nearest rank, the same convention the session record uses
    /// so that a number here and a number there can be read side by side.
    /// </summary>
    public static MacGateCost Summarise(string name, double[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Length == 0)
            return new MacGateCost(name, 0, 0, 0, 0, 0, 0);

        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);

        return new MacGateCost(
            name, sorted.Length, sorted[0], samples.Average(),
            Percentile(sorted, 0.50), Percentile(sorted, 0.99), sorted[^1]);
    }

    /// <summary>Ceil(p * n), so at p99 exactly one per cent may sit above it.</summary>
    private static double Percentile(double[] sorted, double p)
    {
        int rank = (int)Math.Ceiling(p * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }

    /// <summary>The two report lines, which name the copy so neither number is read as the gate alone.</summary>
    public static string Describe(MacGateComparison comparison)
    {
        var c = CultureInfo.InvariantCulture;
        return $"[replay] MAC gate timing over {comparison.Datagrams.ToString(c)} head(s), "
            + $"{comparison.Managed.Batches.ToString(c)} pass(es) each, us per head:\n"
            + Line(comparison.Managed) + Line(comparison.Native) + Line(comparison.Copy)
            + "[replay]   the copy is in both of the two above it and in neither's difference\n";

        static string Line(MacGateCost cost)
        {
            var c = CultureInfo.InvariantCulture;
            return $"[replay]   {cost.Name,-8} min {cost.MinUs.ToString("F3", c)}  "
                + $"mean {cost.MeanUs.ToString("F3", c)}  p50 {cost.P50Us.ToString("F3", c)}  "
                + $"p99 {cost.P99Us.ToString("F3", c)}  max {cost.MaxUs.ToString("F3", c)}\n";
        }
    }
}
