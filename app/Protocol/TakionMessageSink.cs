using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP748: the run's built messages, put on the takion's socket.
///
/// PP684 built the four messages the stream connection sends and PP424 the three before them, all
/// through <see cref="IStreamMessageSink"/> - a seam that exists so a builder needs no socket and no
/// takion. Nothing outside the test project implemented it, because nothing could: the takion had
/// no send. It has one now, and this is the two lines between them.
///
/// PP778: THE DATA TYPE IS THE CHANNEL, and this file had it as the flags.
///
/// The sentence here used to say the C "passes it to the same send as type_b", and that is the
/// wrong argument. <c>chiaki_takion_send_message_data(takion, chunk_flags, channel, ...)</c> takes
/// the flags FIRST, and <c>stream_connection_send_data</c> calls it as <c>(takion, 1, data_type,
/// ...)</c> - so the flags are always one and the data type goes in the channel field, at payload+4.
/// The byte at payload+8 that the receive side reads as a type is written as zero by every send.
///
/// So this sink held a channel of zero for every message and put the data type where the flags go.
/// Nothing refuses that: the transport is fine, the C's own receiver warns about a type_b that is
/// not one and pushes anyway, and a console acknowledges each message and acts on none. Two live
/// trials read exactly that - a BIG acked twice and never answered.
///
/// A REFUSED SEND IS FALSE AND NOT A THROW. The IDR request's caller reads what Send returns, which
/// is the only reason the seam's method has a bool at all - so a takion that has not connected, or
/// a socket that refused the datagram, has to arrive there as false.
/// </summary>
public sealed class TakionMessageSink(ManagedTakion takion) : IStreamMessageSink
{
    /// <summary>
    /// The chunk flags the stream connection's ORDINARY sends carry, which is one.
    ///
    /// PP831: this used to say the C has no call passing anything else, and names both of the
    /// BIG's senders as passing 1. That is false and was false when PP827 added the fragment loop:
    /// the C's non-final fragments pass ZERO - <c>(&amp;takion, 0, 1, buf + buf_pos, ...)</c> - and
    /// only the last passes 1. <see cref="SendBig"/> has always got that right; the sentence here
    /// did not, which is the same kind of claim about a C argument that PP778 and PP813 both cost
    /// a live run to correct.
    ///
    /// So it is a constant for <see cref="Send"/> alone, where stream_connection_send_data's own
    /// literal 1 is the whole of the C's behaviour.
    /// </summary>
    public const byte StreamChunkFlags = TakionDataPush.ExpectedTypeB;

    /// <summary>How many messages this sink has handed to the takion.</summary>
    public int Offered { get; private set; }

    /// <summary>And how many of those the socket took.</summary>
    public int Sent { get; private set; }

    /// <summary>The last outcome, so a caller can see which stage a refusal reached.</summary>
    public TakionSendOutcome? Last { get; private set; }

    /// <summary>
    /// PP827: the usable MTU the BIG is cut against - already less the C's fifty bytes.
    ///
    /// THE C TAKES THE SMALLER OF THE TWO MEASUREMENTS AND THEN SUBTRACTS: `mtu = min(mtu_in,
    /// mtu_out); mtu -= 50;`. Both halves matter - senkusha measures the directions separately and
    /// they need not agree, so using the inbound one alone would cut against a link the BIG is not
    /// travelling on. <see cref="UsableMtu"/> is that rule, in one place.
    ///
    /// The default is what a session uses when senkusha fails outright, which is the ceiling of its
    /// own search less the same fifty. A sink nobody told is therefore on the C's own fallback
    /// rather than on an optimistic number - and PP827 measured that this is the case where the
    /// loop runs zero times, so the default is also the behaviour this sink had before.
    /// </summary>
    public int Mtu { get; set; } =
        (int)SenkushaMeasurements.MtuMax - BigFragments.NetworkOverhead;

    /// <summary>The C's own two steps: the smaller of the measurements, less the network overhead.</summary>
    public static int UsableMtu(uint mtuIn, uint mtuOut)
        => (int)Math.Min(mtuIn, mtuOut) - BigFragments.NetworkOverhead;

    /// <inheritdoc/>
    public bool Send(in StreamMessage message)
    {
        ArgumentNullException.ThrowIfNull(message.Body);

        Offered++;

        // The data type as the CHANNEL and the flags as the constant they are, which is the C's own
        // (takion, 1, data_type, ...) read left to right.
        TakionSendOutcome outcome = takion.SendData(message.DataType, message.Body, StreamChunkFlags);
        Last = outcome;

        if (outcome.Error != ChiakiError.Success)
            return false;

        Sent++;
        return true;
    }

    /// <summary>
    /// PP827: stream_connection_send_big's loop, which this port did not have.
    ///
    /// THE FLAG IS NOT THE CONSTANT THE OTHER SEND USES, and that is the whole hazard. Every send in
    /// <see cref="Send"/> passes 1 because the C's chokepoint does; the BIG's fragments pass ZERO
    /// and only the last passes 1. That one bit is the end-of-message marker the console waits on -
    /// a loop that passed 1 throughout would end the message on the first fragment, and one that
    /// passed 0 throughout would leave the console waiting forever with no error on either side.
    /// <see cref="BigFragments"/> decides which fragment carries it, and PP376 is the boundary case
    /// that made that a separate module rather than a comparison written here.
    ///
    /// IT STOPS AT THE FIRST REFUSAL. A partial BIG cannot be completed by sending the rest of it
    /// later, so there is nothing to gain by continuing - which is the C's own behaviour and the
    /// reason it counts fragments while it goes.
    ///
    /// ONE FRAGMENT IS THE ORDINARY CASE. At the fallback MTU the plan is a single message that
    /// ends the message, so this takes the same path <see cref="Send"/> would and puts the same
    /// bytes on the wire - which is why every stream that has worked went on working.
    /// </summary>
    public bool SendBig(in StreamMessage message)
    {
        ArgumentNullException.ThrowIfNull(message.Body);

        byte[] body = message.Body;
        IReadOnlyList<BigFragment> plan = BigFragments.Plan(body.Length, Mtu);

        foreach (BigFragment fragment in plan)
        {
            Offered++;

            ReadOnlySpan<byte> slice = body.AsSpan(fragment.Offset, fragment.Size);

            // Zero on every fragment but the last, which is the C's 0-then-1 across its two sends.
            byte flags = fragment.EndsTheMessage ? StreamChunkFlags : (byte)0;

            TakionSendOutcome outcome = fragment.IsFirst
                ? takion.SendData(message.DataType, slice, flags)
                : takion.SendDataContinuation(message.DataType, slice, flags);

            Last = outcome;

            if (outcome.Error != ChiakiError.Success)
                return false;

            Sent++;
        }

        return true;
    }
}
