namespace ChiakiNg.Protocol;

/// <summary>
/// PP680: what a parse asks for a full key position, whichever ledger is behind it.
///
/// The shim's <see cref="KeyState"/> and PP677's <see cref="ManagedKeyState"/> answer the same
/// question and are held to each other over four thousand real positions. The parse does not care
/// which it has, and the interface is what lets a session run on the managed one while the
/// differential still drives the native one - so neither is written into the parse's signature.
/// </summary>
public interface IKeyPositionLedger
{
    /// <summary>The sixty-four-bit position a thirty-two-bit one on the wire means.</summary>
    /// <param name="commit">Whether this request advances the state.</param>
    ulong RequestPos(uint low, bool commit = true);
}

/// <summary>
/// PP677: chiaki_key_state_request_pos, in managed code.
///
/// The counter every encrypted byte of a session is keyed by. The wire carries thirty-two bits and
/// the cipher needs sixty-four, so the high half is remembered here and moved when the low half
/// wraps - which is the whole of the function and the whole of what a port can get wrong.
///
/// TWO COMPARISONS HAVE TO DISAGREE. Neither branch fires on the RFC comparison alone: the wrap up
/// needs <c>gt(low, prev)</c> AND <c>low &lt; prev</c>, the wrap down needs <c>lt(low, prev)</c> AND
/// <c>low &gt; prev</c>. That pairing is the wrap detector - the sequence comparison says which is
/// newer across the wrap, the plain one says the numbers went the other way, and only both together
/// mean the counter crossed 2^32. A port testing either alone adds or subtracts 2^32 on ordinary
/// traffic.
///
/// AND A REPEAT MUST DO NEITHER. Two consecutive packets can carry the same position - twenty-six
/// times in the first two thousand of PP519's capture - and <see cref="SeqNum.Gt"/> and
/// <see cref="SeqNum.Lt"/> are both false of a value against itself. An expansion written with
/// <c>&gt;=</c> anywhere would add 2^32 to every one of those and decrypt nothing afterwards.
///
/// PEEK IS NOT AN OPTIMISATION. A parse that may still turn out to be garbage asks without
/// committing, so a corrupt packet cannot drag the counter forward: chiaki_takion_packet_read_key_pos
/// peeks before the MAC gate, while takion_parse_message and av_packet_parse commit. Getting that
/// backwards desynchronises the cipher on the first packet that fails its MAC.
///
/// THE DECREMENT IS GUARDED. <c>high</c> is only moved down when it is non-zero, so a session's
/// opening - where the console sends zero until the cipher exists - cannot produce a position below
/// zero by wrapping the high half to 0xffffffff.
/// </summary>
public sealed class ManagedKeyState : IKeyPositionLedger
{
    /// <summary>The sixty-four-bit position last committed. Zero is chiaki_key_state_init.</summary>
    public ulong Previous { get; private set; }

    /// <summary>
    /// The position <paramref name="low"/> expands to, committed or only peeked at.
    /// </summary>
    /// <param name="low">The thirty-two bits the wire carried.</param>
    /// <param name="commit">
    /// Whether the result becomes the state. False is what a parse uses before it has decided the
    /// packet is real.
    /// </param>
    public ulong RequestPos(uint low, bool commit = true)
    {
        var prevLow = (uint)Previous;
        var high = (uint)(Previous >> 32);

        if (SeqNum.Gt(low, prevLow) && low < prevLow)
            high++;
        else if (SeqNum.Lt(low, prevLow) && low > prevLow && high != 0)
            high--;

        ulong result = ((ulong)high << 32) | low;

        if (commit)
            Previous = result;

        return result;
    }

    /// <summary>
    /// chiaki_key_state_commit: set the state outright, without expanding anything.
    ///
    /// What a caller uses after a peek it has decided to keep. The shim does not wrap this one, so
    /// it has no oracle - and it needs none, being an assignment the header states.
    /// </summary>
    public void Commit(ulong previous) => Previous = previous;
}
