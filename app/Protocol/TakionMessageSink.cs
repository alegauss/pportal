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
/// THE DATA TYPE RIDES WITH THE BYTES. <see cref="StreamMessage"/> carries it because it varies per
/// message and is decided by the builder, and the C passes it to the same send as type_b. So this
/// hands it through rather than choosing one, and the only thing it decides is the channel.
///
/// A REFUSED SEND IS FALSE AND NOT A THROW. The IDR request's caller reads what Send returns, which
/// is the only reason the seam's method has a bool at all - so a takion that has not connected, or
/// a socket that refused the datagram, has to arrive there as false.
/// </summary>
public sealed class TakionMessageSink(ManagedTakion takion, ushort channel = TakionMessageSink.StreamChannel)
    : IStreamMessageSink
{
    /// <summary>The channel the stream connection's own messages go out on.</summary>
    public const ushort StreamChannel = 0;

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

        TakionSendOutcome outcome = takion.SendData(channel, message.Body, message.DataType);
        Last = outcome;

        if (outcome.Error != ChiakiError.Success)
            return false;

        Sent++;
        return true;
    }
}
