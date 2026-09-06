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
    /// The chunk flags every one of the stream connection's sends carries, which is one.
    ///
    /// Not a parameter, because the C has no call that passes anything else: the literal 1 is
    /// written into stream_connection_send_data and into both of the BIG's two senders.
    /// </summary>
    public const byte StreamChunkFlags = TakionDataPush.ExpectedTypeB;

    /// <summary>How many messages this sink has handed to the takion.</summary>
    public int Offered { get; private set; }

    /// <summary>And how many of those the socket took.</summary>
    public int Sent { get; private set; }

    /// <summary>The last outcome, so a caller can see which stage a refusal reached.</summary>
    public TakionSendOutcome? Last { get; private set; }

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
}
