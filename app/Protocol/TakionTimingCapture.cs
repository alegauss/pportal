namespace ChiakiNg.Protocol;

/// <summary>One datagram as a timing run needs it.</summary>
/// <param name="ArrivalMicroseconds">Since the first datagram of the capture, not wall clock.</param>
/// <param name="Length">The whole datagram's length, whatever was kept of it.</param>
/// <param name="BaseType">The low nibble of byte zero - what PP490's dispatch decides on.</param>
/// <param name="Head">The first bytes, truncated. Never the payload.</param>
public readonly record struct CapturedDatagram(
    long ArrivalMicroseconds, int Length, int BaseType, byte[] Head);

/// <summary>Why a capture stopped taking datagrams.</summary>
public enum CaptureEnd
{
    /// <summary>Still taking.</summary>
    Open,

    /// <summary>The datagram count reached its bound.</summary>
    Full,

    /// <summary>The window's duration elapsed.</summary>
    Elapsed,
}

/// <summary>
/// PP510, under PP27: what a timing run has to capture, decided before the session that fills it.
///
/// PP27's remaining half is one run against the C, and PP501 declared what it waits on: a live
/// console, because takion's datagrams have to be recorded. Nothing records one - and deciding what
/// to record is not what a session should be spent on.
///
/// THE EXCHANGE CORPUS IS THE WRONG ARTEFACT, AND PP420 SAYS WHY. Its discriminator is whether a
/// count belongs to the protocol: a handshake message occurs a bounded number of times because the
/// protocol says so, a heartbeat as often as the session was open. Datagrams are entirely the
/// second kind - their count, their sizes and their spacing are the network's - so a corpus built
/// to be replayed message-for-message has nothing to hold them with.
///
/// SO THIS KEEPS FOUR THINGS AND NOT THE PAYLOAD. Arrival relative to the first datagram, length,
/// base type, and a truncated head. <see cref="TakionDispatch"/> decides on byte zero and
/// <see cref="TakionPacketMac"/> on fields inside the first eighteen, so <see cref="HeadBytes"/>
/// answers every question either model asks - and carries no frame of anybody's screen.
///
/// AND IT IS BOUNDED AT CAPTURE RATHER THAN FILTERED AFTER. A console at sixty frames a second
/// sends thousands of datagrams a second; a capture that runs until the session ends is a file
/// nobody opens and a decision nobody made. Two bounds, because either alone is the wrong one: a
/// count alone captures a burst, a duration alone captures whatever a quiet link gave.
///
/// What needs the console is filling it. The shape is settled here, so that session is one session
/// rather than three.
/// </summary>
public sealed class TakionTimingCapture
{
    /// <summary>
    /// How much of each datagram is kept.
    ///
    /// Eighteen is what PP497's gate reads at its furthest - the key position of a video or audio
    /// packet ends at offset eighteen - so a head of that length answers the dispatch and the MAC
    /// layout both. Nothing downstream of those needs a byte, and every byte past here is content.
    /// </summary>
    public const int HeadBytes = 18;

    /// <summary>
    /// PP616: enough to keep any datagram whole, for a capture PP613's relay fills.
    ///
    /// Two thousand and not a computed MTU: a takion datagram on a LAN is under 1500 and the
    /// number here only has to be an upper bound, since <see cref="Offer"/> keeps the smaller of
    /// this and what arrived. Deriving it from an interface's MTU would make the width depend on
    /// which adapter a run happened to use, which is the sort of thing a capture should not carry.
    /// </summary>
    public const int WholeDatagramBytes = 2048;

    /// <summary>
    /// How many datagrams a capture takes before it stops.
    ///
    /// PP526: derived from the default window at the rate a real session sent, so the two bounds
    /// describe one sample length rather than two numbers that were picked apart.
    /// </summary>
    public const int DefaultLimit = SampleWindow.DefaultSeconds * SampleWindow.DatagramsPerSecond;

    /// <summary>How long a capture runs before it stops, whichever comes first.</summary>
    public const long DefaultWindowMicroseconds = SampleWindow.DefaultSeconds * 1_000_000L;

    private readonly List<CapturedDatagram> datagrams = [];
    private long? firstArrival;

    /// <summary>The count bound for this capture.</summary>
    public int Limit { get; }

    /// <summary>The duration bound for this capture.</summary>
    public long WindowMicroseconds { get; }

    /// <summary></summary>
    public TakionTimingCapture(
        int limit = DefaultLimit,
        long windowMicroseconds = DefaultWindowMicroseconds,
        int keepBytes = HeadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowMicroseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keepBytes);

        Limit = limit;
        WindowMicroseconds = windowMicroseconds;
        KeepBytes = keepBytes;
    }

    /// <summary>
    /// PP615: how much of each datagram this keeps, which is not always the tap's eighteen.
    ///
    /// <see cref="HeadBytes"/> is the right default and PP510 gives its reason - it answers the
    /// dispatch and the MAC layout, and carries no frame of anybody's screen. It is also the width
    /// the C hands over, so a capture fed by the tap could keep no more if it wanted to.
    ///
    /// PP613's relay is not the tap. It carries whole datagrams because it forwards them, and a
    /// capture that truncated them again would throw away the only thing that recording was for -
    /// the payloads Â§PP27's remaining half needs, which PP612 established cannot be got from the C
    /// without the patch a non-goal refuses.
    ///
    /// SO THE WIDTH IS THE CALLER'S NOW, and the caller says what it is for. Whatever is above the
    /// default is content, and a run that keeps it should be one somebody asked for.
    /// </summary>
    public int KeepBytes { get; private init; }

    /// <summary>PP526: a capture of an asked-for length, whose bounds were settled together.</summary>
    public TakionTimingCapture(SampleBounds bounds, int keepBytes = HeadBytes)
        : this(bounds.Limit, bounds.WindowMicroseconds, keepBytes)
    {
    }

    /// <summary>What has been captured, in arrival order.</summary>
    public IReadOnlyList<CapturedDatagram> Datagrams => datagrams;

    /// <summary>Why it stopped, or that it has not.</summary>
    public CaptureEnd End { get; private set; } = CaptureEnd.Open;

    /// <summary>How many datagrams arrived after the capture closed.</summary>
    public int Missed { get; private set; }

    /// <summary>
    /// Offers one datagram to the capture.
    /// </summary>
    /// <param name="datagram">The bytes as they arrived. Only the head is kept.</param>
    /// <param name="arrivalMicroseconds">
    /// A monotonic reading. The first one taken becomes the origin, so the file carries no wall
    /// clock and two captures can be compared without one.
    /// </param>
    /// <param name="datagramLength">
    /// PP515: the WHOLE datagram's length, which is not <paramref name="datagram"/>'s.
    ///
    /// The tap truncates to the head before this sees anything, so a capture measuring what it was
    /// handed recorded 18 for every packet of its first real run. The length now arrives beside the
    /// bytes - the tap's type field carries it - and null means "as long as what was handed over",
    /// which is what a caller with a whole datagram in hand should pass.
    /// </param>
    /// <returns>Whether it was taken.</returns>
    public bool Offer(ReadOnlySpan<byte> datagram, long arrivalMicroseconds, int? datagramLength = null)
    {
        if (datagram.IsEmpty)
            return false;

        if (End != CaptureEnd.Open)
        {
            Missed++;
            return false;
        }

        firstArrival ??= arrivalMicroseconds;
        long since = arrivalMicroseconds - firstArrival.Value;

        // Checked before taking, so the window is a bound on what is IN the capture rather than on
        // what was offered to it.
        if (since > WindowMicroseconds)
        {
            End = CaptureEnd.Elapsed;
            Missed++;
            return false;
        }

        int head = Math.Min(KeepBytes, datagram.Length);

        // Never shorter than what was handed over: a length that undercut the head would describe a
        // datagram smaller than the bytes recorded from it.
        int length = Math.Max(datagramLength ?? datagram.Length, datagram.Length);

        datagrams.Add(new CapturedDatagram(
            since,
            length,
            datagram[0] & TakionDispatch.BaseTypeMask,
            datagram[..head].ToArray()));

        if (datagrams.Count >= Limit)
            End = CaptureEnd.Full;

        return true;
    }

    /// <summary>
    /// The gaps between arrivals, which is what a timing run actually compares.
    ///
    /// One shorter than the capture, and empty for a capture of one - a single datagram has no
    /// spacing, and reporting a zero there would be a measurement nobody took.
    /// </summary>
    public IReadOnlyList<long> InterArrivalMicroseconds()
        => [.. datagrams.Zip(datagrams.Skip(1), (a, b) => b.ArrivalMicroseconds - a.ArrivalMicroseconds)];

    /// <summary>How many of each base type the capture holds.</summary>
    public IReadOnlyDictionary<int, int> ByBaseType()
        => datagrams
            .GroupBy(d => d.BaseType)
            .ToDictionary(g => g.Key, g => g.Count());
}
