using System.Buffers.Binary;

namespace ChiakiNg.Protocol;

/// <summary>What the data handler did with one message.</summary>
public enum TakionDataPushVerdict
{
    /// <summary>Payload under nine bytes: dropped, and the datagram freed. PP491's reachable leak.</summary>
    TooShort,

    /// <summary>Built into an entry and pushed onto the data queue.</summary>
    Pushed,
}

/// <summary>What one data message became.</summary>
/// <param name="Verdict">Pushed, or dropped for being too short.</param>
/// <param name="Entry">The entry, where one was built. <see cref="TakionDataDrain"/> reads the same type.</param>
/// <param name="WarnedOnTypeB">Whether the C would have logged the type_b warning.</param>
/// <param name="FreesTheDatagram">Whether this path releases the buffer rather than handing it on.</param>
public readonly record struct TakionDataPushReading(
    TakionDataPushVerdict Verdict, TakionDataEntry Entry, bool WarnedOnTypeB, bool FreesTheDatagram);

/// <summary>
/// PP674: takion_handle_packet_message_data, which is the push the data queue was missing.
///
/// PP673 routes a DATA message here and PP493 modelled the drain that follows. Between them was
/// nothing: the entry the C builds from the payload's first six bytes, and the push onto the
/// thirty-two-bit queue that <see cref="ReorderQueue.Wide"/> now is.
///
/// THE TWO DROPS ARE PP491'S AND THEY ARE ABOUT OWNERSHIP. The data arm is the one of the switch's
/// three that does NOT free after the call - it hands the datagram to this function, which puts it
/// in an entry that takion_data_drop frees later. So both early returns free it themselves, and a
/// port that modelled them as plain returns would model a leak.
///
/// THE SHORT ONE IS REACHABLE. The parse forces the datagram to be payload plus twelve and refuses
/// anything under sixteen, so a payload lands under nine only for a tagged datagram of 17 to 25
/// bytes - and before the remote crypt exists the MAC gate passes everything, so a corrupt control
/// packet arriving then leaks one datagram each. The allocation failure is the other, unreachable
/// short of exhaustion and kept symmetric rather than argued about.
///
/// TYPE_B IS A WARNING AND NOT A REFUSAL. The C logs when it is not one and carries on, so a
/// message with any other value is still pushed. Reported here rather than swallowed, because a
/// port that dropped those would be quieter than the C and wrong in a way no test of the happy path
/// would show.
/// </summary>
public static class TakionDataPush
{
    /// <summary>The header inside a data payload: four bytes of sequence, two of channel, three more.</summary>
    public const int DataHeaderSize = 9;

    /// <summary>Where the sequence number sits in the payload.</summary>
    public const int SeqNumOffset = 0;

    /// <summary>And the channel, four bytes in.</summary>
    public const int ChannelOffset = 4;

    /// <summary>The value the C expects in type_b, warning about anything else.</summary>
    public const byte ExpectedTypeB = 1;

    /// <summary>
    /// Read one DATA message into the entry the C would build, or drop it.
    /// </summary>
    /// <param name="datagram">The whole datagram, so the entry's offsets name into the caller's buffer.</param>
    /// <param name="payloadOffset">Where the message's payload starts, as PP673's reading gives it.</param>
    /// <param name="payloadSize">Its length, the addend already off.</param>
    /// <param name="typeB">The chunk flags, which the C calls type_b here.</param>
    public static TakionDataPushReading Read(
        ReadOnlySpan<byte> datagram, int payloadOffset, int payloadSize, byte typeB)
    {
        bool warned = typeB != ExpectedTypeB;

        if (payloadSize < DataHeaderSize
            || payloadOffset < 0
            || payloadOffset + payloadSize > datagram.Length)
        {
            return new TakionDataPushReading(
                TakionDataPushVerdict.TooShort, default, warned, FreesTheDatagram: true);
        }

        ReadOnlySpan<byte> payload = datagram.Slice(payloadOffset, payloadSize);

        // Copied, because the entry outlives the call - the C hands it the datagram's own bytes and
        // takion_data_drop frees them later, which over a pooled buffer is the copy PP493's model
        // already takes.
        var entry = new TakionDataEntry(
            BinaryPrimitives.ReadUInt32BigEndian(payload[SeqNumOffset..]),
            payload.ToArray(),
            typeB,
            BinaryPrimitives.ReadUInt16BigEndian(payload[ChannelOffset..]));

        // Pushed, so the datagram is the ENTRY's now and this path frees nothing.
        return new TakionDataPushReading(
            TakionDataPushVerdict.Pushed, entry, warned, FreesTheDatagram: false);
    }

    /// <summary>
    /// Read a message and push it onto a queue, which is the C's last two lines before the drain.
    /// </summary>
    /// <returns>What was read, so a caller can see the drop as well as the push.</returns>
    public static TakionDataPushReading ReadAndPush(
        ReadOnlySpan<byte> datagram, int payloadOffset, int payloadSize, byte typeB, ReorderQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        TakionDataPushReading reading = Read(datagram, payloadOffset, payloadSize, typeB);

        if (reading.Verdict == TakionDataPushVerdict.Pushed)
            queue.Push(reading.Entry.SeqNum, payloadOffset);

        return reading;
    }
}
