using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP485, under PP27: the per-datagram allocation, which takion.c does twice and this does not do
/// at all.
///
/// §PP27 calls this the one task in the block where the runtime is a genuine risk rather than a
/// prejudice: a pause at the wrong moment is a dropped frame, and the traffic is thousands of small
/// packets a second, each of which is an allocation if written carelessly. The C is the careless
/// one, and says so on the line - takion.c's receive loop reads
///
/// <code>
///   size_t received_size = 1500;
///   uint8_t *buf = malloc(received_size); // TODO: no malloc?
/// </code>
///
/// and then reallocs it down to the received length once the datagram is in. Two heap operations per
/// packet, on the thread the whole stream rides on, with upstream's own question mark on the first.
///
/// THE REALLOC IS NOT A DECISION, IT IS A LANGUAGE. A C pointer does not carry its length, so
/// shrinking the block is how the loop tells the handler how many bytes arrived. A Span carries its
/// own length, so the same fact costs nothing here - which is why this is a port of what the C means
/// and not a redesign of what it does. "No redesign while porting" is a non-goal of this project,
/// and dropping a malloc that exists to express a length is not one.
///
/// So the buffer is rented once for the loop and reused, and the receive path allocates nothing per
/// datagram. The one thing that cannot be reused is a datagram the loop RETAINS: the postpone path
/// holds packets until crypt initialises, and those outlive the iteration that received them. That
/// path copies, and <see cref="Retain"/> is where - which is also the ownership the C got wrong
/// until PP474 freed them.
///
/// This does not port the receive loop itself, only the buffer under it. The timing half of PP27 -
/// both implementations over one captured stream - still wants a capture.
/// </summary>
public sealed partial class TakionReceiveBuffer : IDisposable
{
    /// <summary>
    /// The datagram ceiling, as takion.c's receive loop sets it.
    ///
    /// 1500 because that is the ethernet MTU the C picked, not because anything here needs it to be.
    /// <see cref="CapacityInTheC"/> reads the same number out of the file so the two cannot drift.
    /// </summary>
    public const int DatagramCapacity = 1500;

    private byte[]? _rented = ArrayPool<byte>.Shared.Rent(DatagramCapacity);
    private int _length;

    /// <summary>Where a socket receives into: the whole datagram ceiling, and never more.</summary>
    /// <remarks>
    /// The pool may hand back a larger array than asked for. Exposing only the ceiling keeps the
    /// managed loop unable to accept a datagram the C would have truncated, which would be a
    /// difference in behaviour rather than in allocation.
    /// </remarks>
    public Span<byte> Free => Rented.AsSpan(0, DatagramCapacity);

    /// <summary>How many bytes the last receive put in, before anything reads them.</summary>
    public int Length => _length;

    /// <summary>The datagram itself - what the C's realloc'd pointer stood for.</summary>
    public ReadOnlySpan<byte> Datagram => Rented.AsSpan(0, _length);

    /// <summary>
    /// PP703: the same bytes, mutable, which is what the dispatch is handed.
    ///
    /// The C's handler takes <c>uint8_t *</c> and the AV branch takes ownership of it, so a port
    /// that could only offer a read-only view would have to copy before the one branch that decrypts
    /// in place. Separate from <see cref="Datagram"/> rather than replacing it: a reader that only
    /// reads should say so, and every other caller here is one.
    /// </summary>
    public Span<byte> Writable => Rented.AsSpan(0, _length);

    private byte[] Rented =>
        _rented ?? throw new ObjectDisposedException(nameof(TakionReceiveBuffer));

    /// <summary>Records how many bytes arrived.</summary>
    /// <param name="length">The received length, which cannot exceed the ceiling above.</param>
    public void Received(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, DatagramCapacity);

        _ = Rented;
        _length = length;
    }

    /// <summary>
    /// A copy of the datagram, sized to it, for a path that keeps the packet past this iteration.
    ///
    /// This is the one allocation the receive path is allowed, and it is allowed because retention
    /// is what the postpone path is: the buffer is about to be reused by the next receive, so a
    /// caller that held the span would be reading the wrong packet a millisecond later. Sized to the
    /// datagram rather than to the ceiling, which is what the C's realloc was for.
    /// </summary>
    public byte[] Retain() => Datagram.ToArray();

    /// <summary>
    /// Returns the buffer to the pool, once.
    ///
    /// Idempotent deliberately: returning one array to an ArrayPool twice hands the same memory to
    /// two owners, which is a corruption rather than an error, and the loop this sits under has
    /// several exits.
    /// </summary>
    public void Dispose()
    {
        if (_rented is not { } rented)
            return;

        _rented = null;
        ArrayPool<byte>.Shared.Return(rented);
    }

    /// <summary>The receive loop's own file.</summary>
    /// <remarks>
    /// Reusing <see cref="ReorderQueueSource.TakionRelativePath"/> rather than spelling the path a
    /// third time - BangReachability already has the second copy.
    /// </remarks>
    public static string? LocateTakion()
        => SanitizerSource.LocateRelative(ReorderQueueSource.TakionRelativePath);

    /// <summary>The datagram ceiling the C's receive loop declares, or null if it stopped saying.</summary>
    public static int? CapacityInTheC(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);

        Match match = ReceivedSizeRegex().Match(takionText);
        return match.Success
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Whether the C still allocates once per datagram and then reallocs it down.
    ///
    /// Asserted so the claim above cannot go stale. If upstream ever answers its own TODO, this is
    /// the line that says so - and at that moment the sentence about two heap operations per packet
    /// stops being true and needs rewriting rather than quietly surviving.
    /// </summary>
    public static bool TheCAllocatesPerDatagram(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);

        return takionText.Contains("malloc(received_size)", StringComparison.Ordinal)
            && takionText.Contains("realloc(buf, received_size)", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"received_size\s*=\s*([0-9]+)\s*;")]
    private static partial Regex ReceivedSizeRegex();
}
