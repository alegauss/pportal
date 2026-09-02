namespace ChiakiNg.Protocol;

/// <summary>What one channel carried in a replay, in packets and bytes on the wire.</summary>
/// <param name="Packets">How many datagrams.</param>
/// <param name="WireBytes">How many bytes they were, whole.</param>
public readonly record struct ReplayChannel(int Packets, long WireBytes);

/// <summary>
/// PP633: the takion receive loop, run over datagrams that had a payload.
///
/// PP44 set a zero-allocation budget for the transport and PP113, PP114 and PP176 measured it -
/// every one of them over the frame processor, and every one per packet. The receive loop itself
/// had never been fed a datagram with a body: <see cref="DatagramCorpus"/> keeps eighteen bytes a
/// row on purpose, so the copy the keeping branches do had nothing to copy and the budget was a
/// claim about traffic that did not exist.
///
/// PP616 composed the run that fills it - the relay offering the console's arrivals whole, the
/// writer taking no tap, the session pointed at loopback - and PP617 made it answer a real console.
/// Nobody had replayed one.
///
/// THE CORPUS STILL KEEPS HEADS, and that is not an oversight this closes. PP608 gives the reason:
/// eighteen bytes is committable - no account, no console, no frame - and a whole-datagram capture
/// of a live session is none of those things. So what is kept here is the READING, the way PP610
/// kept the MAC gate's, and the capture stays an artefact of the run that produced it.
/// </summary>
public static class WholeDatagramReplay
{
    /// <summary>When the reading was taken, against PS5-385 over a five-second sample.</summary>
    public const string TakenOn = "2026-09-02";

    /// <summary>How many datagrams the replay read.</summary>
    public const int Datagrams = 4025;

    /// <summary>How long they arrived over, in milliseconds.</summary>
    public const double SpanMillis = 4820.4;

    /// <summary>The mean arrival gap, in microseconds - the spacing every share below is against.</summary>
    public const int MeanGapMicros = 1198;

    /// <summary>Video: the channel the payload is nearly all of.</summary>
    public static ReplayChannel Video { get; } = new(3302, 3685749);

    /// <summary>Audio.</summary>
    public static ReplayChannel Audio { get; } = new(462, 120120);

    /// <summary>Control, which is the only one the head-only corpus could ever have carried whole.</summary>
    public static ReplayChannel Control { get; } = new(261, 8207);

    /// <summary>
    /// What the three branches that KEEP a packet copied, in bytes.
    ///
    /// Less than the wire total because a header is read and not copied, and that difference is the
    /// number this reading exists for: it is the work a head-only replay reports as zero.
    /// </summary>
    public const long BytesCopied = 3693956;

    /// <summary>
    /// What the loop allocated after the warm-up. THE ANSWER IS ZERO, over real payloads.
    ///
    /// PP44's budget, held where it had never been asked: the frame processor's measurements were
    /// about a different stage, and this one is the receive path they sit behind.
    /// </summary>
    public const long BytesAllocatedAfterWarmUp = 0;

    /// <summary>
    /// PP610's MAC gate reading, taken again over whole datagrams and unchanged - which is the
    /// question this run would otherwise leave a reader to re-ask.
    ///
    /// PP497's gate reads to offset eighteen and stops, because the key position of a video or audio
    /// packet ends there. So the gate is a HEAD operation whatever the datagram's length, and a
    /// whole-datagram capture measures it doing exactly the same work.
    /// </summary>
    public const double GateManagedMicros = 0.165;

    /// <summary>The C's half of the same comparison.</summary>
    public const double GateNativeMicros = 0.101;

    /// <summary>The three channels that carried anything.</summary>
    public static IReadOnlyList<ReplayChannel> Channels { get; } = [Video, Audio, Control];

    /// <summary>Every datagram the replay read, added up from the channels.</summary>
    public static int PacketsAcrossChannels => Channels.Sum(one => one.Packets);

    /// <summary>Everything they were on the wire.</summary>
    public static long WireBytesAcrossChannels => Channels.Sum(one => one.WireBytes);

    /// <summary>
    /// What share of the gap the gate spends, which is what PP610 asserted instead of the
    /// microseconds - a second machine keeps the ratio and not the clock.
    /// </summary>
    public static double GateShareOfGap => GateManagedMicros / MeanGapMicros;

    /// <summary>
    /// PP635: why the loop has no ratio of its own, and what stands instead.
    ///
    /// PP27's criterion asked for "the same comparison for the loop, not just the gate". The gate
    /// is comparable because <c>chiaki_takion_packet_mac</c> is a pure function of eighteen bytes -
    /// both sides do identical work and the difference is the runtimes. The loop is not that kind of
    /// thing: PP601 established that takion.c's receive handlers are file-local, and removing a
    /// `static` is the patch a non-goal refuses, so the only C loop that runs is the whole one from
    /// its own socket. Timing that against a managed loop reading a file compares two I/O paths.
    ///
    /// WHAT ANSWERS IT IS HEADROOM. §PP27 called the runtime "a genuine risk rather than a
    /// prejudice" and named what would make it real - a pause at the wrong moment, and thousands of
    /// small packets a second each an allocation if written carelessly. Zero allocated and a
    /// thousandth of the arrival gap spent is that risk measured rather than argued.
    /// </summary>
    public const string WhyTheLoopHasNoRatio =
        "takion's receive handlers are file-local, so the only C loop that runs is bound to a socket";

    /// <summary>
    /// How much of the arrival gap the whole per-datagram job could take and still be nothing.
    ///
    /// A ceiling rather than the measurement, because the measurement is the gate's and the loop's
    /// copy is not separately timed. What the reading supports is the ORDER: under a thousandth.
    /// </summary>
    public const double WorkShareOfGapCeiling = 0.001;
}
