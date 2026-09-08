using ChiakiNg.Native;
using ChiakiNg.Session;

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
/// network error. This is the middle: take the cipher's lock, stamp the packet's MAC, release, send.
///
/// PP811 CORRECTED THE LOCK'S EXTENT, AND THE CORRECTION IS THE OTHER WAY ROUND FROM THE GUESS.
/// This file used to say the C holds gkcrypt_local_mutex across BOTH the MAC and the send, and
/// argued for it: two threads that stamped under a lock and then raced to the socket would put
/// their datagrams on the wire in an order neither key position matches. The argument is
/// reasonable and the C does not make it. chiaki_takion_send locks, calls
/// chiaki_takion_packet_mac, UNLOCKS, and only then calls chiaki_takion_send_raw - three lines
/// apart, and <see cref="TakionSendPathSource"/> now holds that order against the file instead of
/// this paragraph asserting it.
///
/// What the mutex guards is the CIPHER, which is the only shared thing the MAC touches. The wire
/// is a socket and orders its own writes; the key position rides in the packet, so a console
/// reading two of them out of order reads two positions, which is what UDP would do to them
/// anyway.
///
/// THE FEEDBACK PATH IS THE ONE THAT DOES HOLD IT ACROSS THE SEND, which is why the guess was
/// plausible. takion_send_feedback_packet and the microphone send lock, advance, encrypt, sign,
/// send_raw and unlock at `beach` - a genuinely wider critical section, in a different function.
/// <see cref="TakionFeedbackSends.Send"/> is that one and keeps its send inside.
///
/// THE MUTEX IS RECURSIVE IN THE C, which is why this takes a lock object rather than owning one.
/// chiaki_mutex_init(&amp;takion->gkcrypt_local_mutex, true) asks for reentrancy, and the callers that
/// need it are PP676's. A path that owned a private lock would deadlock those.
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
    /// Stamp under <paramref name="cipherLock"/>, then send outside it.
    /// </summary>
    /// <param name="datagram">Mutated in place: the MAC field is written into it.</param>
    /// <param name="gmac">The cipher, or null before crypt exists - which still blanks the field.</param>
    /// <param name="wire">Where it goes.</param>
    /// <param name="cipherLock">
    /// The takion's own gkcrypt_local lock, held across the stamp and NOT across the send. Passed in
    /// because the C's is recursive and shared with callers that already hold it.
    /// </param>
    public static ChiakiError Send(
        Span<byte> datagram, Func<ReadOnlyMemory<byte>, byte[]>? gmac, ITakionWire wire, object cipherLock)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ArgumentNullException.ThrowIfNull(cipherLock);

        TakionPacketMac.MacResult stamped;

        lock (cipherLock)
        {
            // Neither MAC is READ back, which is not the same as neither being asked for and is
            // what this file used to say. chiaki_takion_send passes NULL for mac_old_out and a
            // stack buffer for mac_out that nothing afterwards looks at - so the C spends four
            // bytes of stack and the port would spend an array per send, which is the difference
            // worth keeping.
            stamped = TakionPacketMac.Apply(datagram, gmac, wantMacBefore: false, wantMacAfter: false);
        }

        // Outside the lock, and the early return is outside it too - the C tests the MAC's error
        // after its unlock, so a refusal releases the cipher before it answers.
        if (stamped.Error != ChiakiError.Success)
            return stamped.Error;

        return wire.Send(datagram);
    }
}

/// <summary>
/// PP811: the C's own send, so the lock's extent is read out of takion.c and never argued for.
///
/// This exists because the argument was wrong. PP675 reasoned its way to a wider critical section
/// than the C has, wrote the reasoning into the prose, and nothing could contradict it - the module
/// had no caller, so no behaviour disagreed either. A check over the three lines is what a
/// paragraph could not be.
/// </summary>
public static class TakionSendPathSource
{
    /// <summary>takion.c, through the path constant this port already has.</summary>
    public static string? Locate() => TakionReceiveBuffer.LocateTakion();

    /// <summary>chiaki_takion_send's body, or null where it is gone.</summary>
    public static string? SendBody(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);

        return CFunction.Body(
            takionText, "CHIAKI_EXPORT ChiakiErrorCode chiaki_takion_send(");
    }

    /// <summary>
    /// Whether the cipher is still released BEFORE the raw send, which is the whole finding.
    ///
    /// Three positions in one body: the lock, the unlock, and the send. If a later upstream edit
    /// moves the unlock past the send, the C has adopted the wider section this port was written
    /// with first - and then this turns red rather than the port quietly being right by accident.
    /// </summary>
    public static bool TheCipherIsReleasedBeforeTheRawSend(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        int locked = sendBody.IndexOf(
            "chiaki_mutex_lock(&takion->gkcrypt_local_mutex)", StringComparison.Ordinal);
        int unlocked = sendBody.IndexOf(
            "chiaki_mutex_unlock(&takion->gkcrypt_local_mutex)", StringComparison.Ordinal);
        int sent = sendBody.IndexOf("chiaki_takion_send_raw(", StringComparison.Ordinal);

        return locked >= 0 && unlocked > locked && sent > unlocked;
    }

    /// <summary>
    /// And that the MAC is taken while it IS held, which is the half the lock is actually for.
    ///
    /// Without this the check above would pass a body that never locked around anything.
    /// </summary>
    public static bool TheMacIsTakenUnderTheCipher(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        int locked = sendBody.IndexOf(
            "chiaki_mutex_lock(&takion->gkcrypt_local_mutex)", StringComparison.Ordinal);
        int mac = sendBody.IndexOf("chiaki_takion_packet_mac(", StringComparison.Ordinal);
        int unlocked = sendBody.IndexOf(
            "chiaki_mutex_unlock(&takion->gkcrypt_local_mutex)", StringComparison.Ordinal);

        return locked >= 0 && mac > locked && unlocked > mac;
    }

    /// <summary>
    /// Whether the feedback path still holds the cipher across ITS raw send, which is the contrast.
    ///
    /// The two together are the finding: one send releases before the socket and one does not, in
    /// the same file. A port that read either rule for both would be wrong about one of them.
    /// </summary>
    public static bool TheFeedbackPathStillHoldsItAcrossTheSend(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);

        if (CFunction.Body(takionText, "static ChiakiErrorCode takion_send_feedback_packet(")
            is not { } body)
        {
            return false;
        }

        int sent = body.IndexOf("chiaki_takion_send_raw(", StringComparison.Ordinal);
        int unlocked = body.IndexOf(
            "chiaki_mutex_unlock(&takion->gkcrypt_local_mutex)", StringComparison.Ordinal);

        return sent >= 0 && unlocked > sent;
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

