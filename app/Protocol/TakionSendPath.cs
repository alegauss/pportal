using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Where a datagram goes once it is stamped.</summary>
public interface ITakionWire
{
    /// <summary>Send one datagram whole. The C's chiaki_takion_send_raw.</summary>
    ChiakiError Send(ReadOnlySpan<byte> datagram);
}

/// <summary>
/// PP675: chiaki_takion_send, which is the one place a takion datagram becomes bytes on a socket.
///
/// Above it the three builders write layouts; below it the raw send turns a negative result into a
/// network error. This is the middle: take the cipher's lock, stamp the packet's MAC, send, release.
///
/// THE LOCK IS THE POINT AND IT IS EASY TO PUT IN THE WRONG PLACE. The C holds
/// gkcrypt_local_mutex across BOTH the MAC and the send, not just the MAC. Two threads that stamped
/// under a lock and then raced to the socket would put their datagrams on the wire in an order
/// neither key position matches, and the console reads the position out of the packet: a stream
/// cipher fed positions out of order produces noise, not an error. So the send is inside.
///
/// THE MUTEX IS RECURSIVE IN THE C, which is why this takes a lock object rather than owning one.
/// chiaki_mutex_init(&amp;takion->gkcrypt_local_mutex, true) asks for reentrancy, and the callers that
/// need it - the feedback and microphone sends, which advance the position and then send while still
/// holding it - are PP676's. A path that owned a private lock would deadlock those.
///
/// NOTHING IS ALLOCATED HERE. The caller's span is stamped in place and handed to the wire, which is
/// what keeps PP44's budget at zero over a stream's worth of sends. <see cref="TakionPacketMac"/>
/// does allocate when a cipher is present - it copies the packet to hand the GMAC a
/// ReadOnlyMemory - and that is its own line's to answer; with no cipher, which is every handshake
/// packet and every send before crypt exists, this path allocates nothing at all.
/// </summary>
public static class TakionSendPath
{
    /// <summary>
    /// Stamp and send, under <paramref name="cipherLock"/>.
    /// </summary>
    /// <param name="datagram">Mutated in place: the MAC field is written into it.</param>
    /// <param name="gmac">The cipher, or null before crypt exists - which still blanks the field.</param>
    /// <param name="wire">Where it goes.</param>
    /// <param name="cipherLock">
    /// The takion's own gkcrypt_local lock, held across the stamp AND the send. Passed in because
    /// the C's is recursive and shared with callers that already hold it.
    /// </param>
    public static ChiakiError Send(
        Span<byte> datagram, Func<ReadOnlyMemory<byte>, byte[]>? gmac, ITakionWire wire, object cipherLock)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(cipherLock);

        lock (cipherLock)
        {
            // Neither MAC is wanted back: chiaki_takion_send passes NULL for both out parameters,
            // and asking for either costs an array per send.
            TakionPacketMac.MacResult stamped =
                TakionPacketMac.Apply(datagram, gmac, wantMacBefore: false, wantMacAfter: false);

            if (stamped.Error != ChiakiError.Success)
                return stamped.Error;

            // Inside the lock. See the note above: stamping under it and sending outside would put
            // two threads' datagrams on the wire in an order their key positions do not match.
            return wire.Send(datagram);
        }
    }
}

/// <summary>
/// A wire that records rather than sends, which is what a test of the path needs.
///
/// PP675: the send path's own behaviour is an ORDER - stamp, then send, both under one lock - and
/// an order is only observable to something that watches both. A socket cannot say when it was
/// called relative to a stamp; this can.
/// </summary>
public sealed class RecordingTakionWire : ITakionWire
{
    private readonly List<byte[]> sent = [];

    /// <summary>What the wire was handed, in order, copied at the moment of the call.</summary>
    public IReadOnlyList<byte[]> Sent => sent;

    /// <summary>What every send returns.</summary>
    public ChiakiError Result { get; set; } = ChiakiError.Success;

    /// <summary>Run on each send before it is recorded, so a test can observe the ordering.</summary>
    public Action<byte[]>? OnSend { get; set; }

    /// <inheritdoc/>
    public ChiakiError Send(ReadOnlySpan<byte> datagram)
    {
        byte[] copy = datagram.ToArray();
        OnSend?.Invoke(copy);
        sent.Add(copy);
        return Result;
    }
}

