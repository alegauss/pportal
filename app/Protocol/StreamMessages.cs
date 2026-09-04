using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP684: one message the stream connection sends, as stream_connection_send_data takes it.
/// </summary>
/// <param name="DataType">
/// takion's data type, which is NOT always one: the streaminfo ack goes as 9 and the two video
/// messages as 2. A builder that produced only a body would lose the distinction, and the console
/// reads it.
/// </param>
/// <param name="PayloadType">
/// The tkproto payload type, which is also the number the message tap records and the recording
/// renders in front of the bytes.
/// </param>
/// <param name="Body">The encoded TakionMessage.</param>
public readonly record struct StreamMessage(byte DataType, ushort PayloadType, byte[] Body);

/// <summary>Where a built message goes, so a builder needs no socket and no takion.</summary>
public interface IStreamMessageSink
{
    /// <summary>
    /// Send one message. Returns whether it went, which is what the IDR request's caller reads.
    /// </summary>
    bool Send(in StreamMessage message);
}

/// <summary>
/// PP684, under PP295: the four messages streamconnection.c sends that had no managed bytes.
///
/// The file sends seven things through one chokepoint - stream_connection_send_data, which takes a
/// data type, a payload type and a body. PP424 and PP425 built three of them: the streaminfo ack,
/// the controller connection and the microphone's own streaminfo. The other four were a signature
/// apiece. Two of those are PP295's second criterion in its own words - a corrupt frame and an IDR
/// request are messages this file sends, and <see cref="ManagedVideoReceiver"/> asks for both
/// through a seam whose only implementations were test doubles.
///
/// THE TRIPLE IS THE UNIT, not the payload. The data type rides with the bytes because it varies per
/// message and is decided here rather than by whatever sends it. That is also where this stops:
/// takion's own framing - the sequence number, the channel, the nine-byte data header - is PP675's,
/// and nothing in this file knows a datagram exists.
///
/// THE THREE THAT EXISTED ARE NOT REBUILT. <see cref="StreamExchangeParticipant"/> writes their
/// bytes and PP424's replay holds them against a recorded exchange; the entries here name their data
/// types and delegate, so the table is complete without a second copy of any message.
///
/// EVERY BODY IS BUILT BY FIELD, which is PP425's rule and the reason it exists: a payload
/// transcribed out of a recording agrees with the recording by construction and can find nothing.
/// The disconnect is the exception that proves it - its bytes ARE checked against a console's, in
/// the test, because there the recording is an independent witness rather than the source.
/// </summary>
public static class StreamMessages
{
    /// <summary>TakionMessage.type, the field every one of these opens with.</summary>
    public const int TypeField = StreamExchangeParticipant.TypeField;

    /// <summary>TakionMessage.corrupt_payload.</summary>
    public const int CorruptPayloadField = 6;

    /// <summary>TakionMessage.disconnect_payload.</summary>
    public const int DisconnectPayloadField = 10;

    /// <summary>CorruptFramePayload.start.</summary>
    public const int CorruptStartField = 1;

    /// <summary>CorruptFramePayload.end.</summary>
    public const int CorruptEndField = 2;

    /// <summary>DisconnectPayload.reason.</summary>
    public const int ReasonField = 1;

    /// <summary>tkproto_TakionMessage_PayloadType_HEARTBEAT.</summary>
    public const ushort HeartbeatType = 3;

    /// <summary>CORRUPTFRAME.</summary>
    public const ushort CorruptFrameType = 5;

    /// <summary>DISCONNECT.</summary>
    public const ushort DisconnectType = 8;

    /// <summary>IDRREQUEST.</summary>
    public const ushort IdrRequestType = 25;

    /// <summary>The data type most of them carry.</summary>
    public const byte OrdinaryData = 1;

    /// <summary>The one the two video messages carry.</summary>
    public const byte VideoData = 2;

    /// <summary>And the one the streaminfo ack carries, alone.</summary>
    public const byte StreamInfoAckData = 9;

    /// <summary>
    /// The reason the client gives, which is a required field and so is written whatever it says.
    ///
    /// Twenty characters, and the length is why they are worth naming: the whole message is 26 bytes
    /// and the console reads the string's length off the wire, so a different reason is a different
    /// datagram rather than a different word.
    /// </summary>
    public const string DisconnectReason = "Client Disconnecting";

    /// <summary>The heartbeat, which is its type and nothing else.</summary>
    public static StreamMessage Heartbeat()
        => new(OrdinaryData, HeartbeatType, ProtobufWriter.Varint(TypeField, HeartbeatType));

    /// <summary>The IDR request, likewise - asking for a keyframe says nothing but which ask it is.</summary>
    public static StreamMessage IdrRequest()
        => new(VideoData, IdrRequestType, ProtobufWriter.Varint(TypeField, IdrRequestType));

    /// <summary>
    /// The corrupt frame report, which is the only one of the four carrying numbers.
    /// </summary>
    /// <param name="start">The first frame lost, as the receiver counted it.</param>
    /// <param name="end">The last. The C sends the pair even where they are equal.</param>
    public static StreamMessage CorruptFrame(ushort start, ushort end)
        => new(
            VideoData,
            CorruptFrameType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, CorruptFrameType),
                ProtobufWriter.Message(
                    CorruptPayloadField,
                    ProtobufWriter.Varint(CorruptStartField, start),
                    ProtobufWriter.Varint(CorruptEndField, end))));

    /// <summary>The disconnect, sent at teardown rather than in answer to anything.</summary>
    public static StreamMessage Disconnect()
        => new(
            OrdinaryData,
            DisconnectType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, DisconnectType),
                ProtobufWriter.Message(
                    DisconnectPayloadField,
                    ProtobufWriter.Bytes(
                        ReasonField, System.Text.Encoding.ASCII.GetBytes(DisconnectReason)))));

    /// <summary>The streaminfo ack, whose bytes are PP424's and whose data type is the odd one.</summary>
    public static StreamMessage StreamInfoAck()
        => new(
            StreamInfoAckData,
            StreamExchangeParticipant.StreamInfoAckType,
            StreamExchangeParticipant.StreamInfoAck());

    /// <summary>The controller connection, likewise.</summary>
    public static StreamMessage ControllerConnection(bool dualSense)
        => new(
            OrdinaryData,
            StreamExchangeParticipant.ControllerConnectionType,
            StreamExchangeParticipant.ControllerConnection(dualSense));

    /// <summary>And this side's microphone streaminfo, which goes out as a STREAMINFO.</summary>
    public static StreamMessage MicrophoneStreamInfo()
        => new(
            OrdinaryData,
            StreamExchangeParticipant.StreamInfo,
            StreamExchangeParticipant.MicrophoneStreamInfo());

    /// <summary>
    /// All seven, so the table can be held against the C's call sites rather than sampled.
    ///
    /// Built once here rather than listed twice: what a drift check compares is this against the
    /// file, and a list written separately from the builders would agree with neither for a while.
    /// </summary>
    public static IReadOnlyList<StreamMessage> All { get; } =
    [
        ControllerConnection(dualSense: false),
        MicrophoneStreamInfo(),
        StreamInfoAck(),
        Disconnect(),
        Heartbeat(),
        CorruptFrame(0, 0),
        IdrRequest(),
    ];
}

/// <summary>
/// PP684: the video receiver's outbound seam, over a sink that takes messages.
///
/// The first implementation of <see cref="IVideoReceiverOutbound"/> that is not a test double.
/// PP291 gave the receiver a four-method seam so its driver need not be a session pointer, and two
/// of those methods are messages streamconnection.c sends; this is what turns them into bytes.
///
/// THE IDR REQUEST'S RETURN IS LOAD-BEARING. ManagedVideoReceiver only starts waiting for a keyframe
/// where the request went out, so a seam that answered true regardless would leave the receiver
/// dropping frames while it waited for an IDR nobody asked for.
///
/// THE FEC FAILURE IS NOT A MESSAGE. The C logs it and the corrupt-frame report beside it is what
/// crosses the wire, so this counts them and sends nothing - a report the caller can read without
/// there being a payload that does not exist.
/// </summary>
/// <param name="sink">Where the two messages go.</param>
public sealed class StreamOutbound(IStreamMessageSink sink) : IVideoReceiverOutbound
{
    private readonly IStreamMessageSink sink =
        sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>How many FEC failures the receiver reported, which is a count and not a message.</summary>
    public int FecFailures { get; private set; }

    /// <summary>The frame index of the last FEC failure, or null where there has been none.</summary>
    public int? LastFecFailure { get; private set; }

    /// <inheritdoc />
    public void SendCorruptFrame(ushort from, ushort to)
    {
        StreamMessage message = StreamMessages.CorruptFrame(from, to);
        sink.Send(in message);
    }

    /// <inheritdoc />
    public bool SendIdrRequest()
    {
        StreamMessage message = StreamMessages.IdrRequest();
        return sink.Send(in message);
    }

    /// <inheritdoc />
    public void FecFailure(int frameIndex, bool idrRequestSent)
    {
        FecFailures++;
        LastFecFailure = frameIndex;
    }
}

/// <summary>
/// PP684: the C's own table of data types, read out of the call sites rather than trusted.
///
/// One comment in streamconnection.c describes the types as "1 for most, 2 for the keyboard pair, 9
/// for the streaminfo ack". The word keyboard appears exactly once in the file - in that comment -
/// and the two sends carrying 2 are the corrupt frame and the IDR request. So this reads the CALLS,
/// which is what the console sees, and the comment is left where it is.
/// </summary>
public static class StreamMessagesSource
{
    /// <summary>The file, through the constant a sibling already spells.</summary>
    public const string RelativePath = StreamSendResults.RelativePath;

    /// <summary>The chokepoint every one of them goes through.</summary>
    public const string Chokepoint = "stream_connection_send_data(stream_connection, ";

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The data type each payload type is sent with, as the file's call sites spell the pair.
    ///
    /// Keyed by the payload type's NAME rather than its number, because the call site writes the
    /// tkproto constant and the number is what this port would otherwise be asserting against
    /// itself.
    /// </summary>
    public static IReadOnlyDictionary<string, byte> DataTypesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var found = new Dictionary<string, byte>(StringComparer.Ordinal);

        for (int at = source.IndexOf(Chokepoint, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(Chokepoint, at + Chokepoint.Length, StringComparison.Ordinal))
        {
            int from = at + Chokepoint.Length;
            int comma = source.IndexOf(',', from);
            if (comma < 0)
                break;

            if (!byte.TryParse(
                    source[from..comma].Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out byte dataType))
            {
                continue;
            }

            int second = source.IndexOf(',', comma + 1);
            if (second < 0)
                break;

            string payload = source[(comma + 1)..second].Trim();
            const string prefix = "tkproto_TakionMessage_PayloadType_";

            if (payload.StartsWith(prefix, StringComparison.Ordinal))
                found[payload[prefix.Length..]] = dataType;
        }

        return found;
    }
}
