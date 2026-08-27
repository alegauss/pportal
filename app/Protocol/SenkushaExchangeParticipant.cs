namespace ChiakiNg.Protocol;

/// <summary>
/// PP421, under PP23: senkusha's handshake, replayed as a conversation.
///
/// PP391 gave ctrl a participant and PP392 gave session one. PP396 recorded senkusha end to end -
/// the one channel whose whole exchange fits in a corpus, because senkusha measures a link and then
/// stops - and nothing replayed it.
///
/// THE LADDER IS DECLARED AND ORDERED. senkusha.c sets STATE_EXPECT_PROTOCOL_ACK and THEN sends the
/// version request; it sets STATE_EXPECT_BANG and THEN sends the BIG. The state before the send,
/// both times, which is what lets an answer arriving immediately be handled rather than dropped. The
/// corpus shows the order the C declares: 001f out, 0020 back, 0000 out, 0001 back.
///
/// THE BIG ANSWERS THE ACK rather than travelling with the request. It goes once the takion version
/// is agreed, and that ordering is most of what this participant is.
///
/// SENKUSHA OPENS ITS OWN CONVERSATION, so <see cref="Opening"/> carries the request. PP392 added
/// that hook for the session channel and this is the second use of it: a capture whose first entry
/// is Sent cannot be replayed by arrivals alone.
///
/// THE MEASUREMENT IS DELIBERATELY NOT MODELLED. The RTT and MTU tests are seven SENKUSHA commands
/// whose number and order depend on the link that was measured - PP375 established that and PP420
/// made it a rule. A participant replaying them would agree only with a run that negotiated the same
/// MTU, which is the opposite of an oracle. So this answers the handshake and says nothing after
/// BANG, and <see cref="Finished"/> is how a caller sees where it stopped.
/// </summary>
public sealed class SenkushaExchangeParticipant : IExchangeParticipant
{
    /// <summary>Whether the BANG has arrived, which is where the handshake ends.</summary>
    public bool Finished { get; private set; }

    /// <summary>Whether the version has been agreed, so the BIG has gone out.</summary>
    public bool VersionAgreed { get; private set; }

    /// <summary>
    /// The two payloads this sends, as senkusha.c encodes them.
    ///
    /// Protobufs rather than the flat arrays ctrl's table holds, so each group is named by the field
    /// it is: a byte wrong here is a message the console reads differently, and the comment is what
    /// makes that reviewable.
    /// </summary>
    public static IReadOnlyDictionary<ushort, byte[]> Payloads { get; } =
        new Dictionary<ushort, byte[]>
        {
            // PP425: built rather than transcribed. senkusha_set_version sets
            // msg.type = TAKIONPROTOCOLREQUEST and a request carrying version 9, and the field
            // numbers are takion.proto's - which is a document a reader can check, where a run of
            // hex out of the corpus is the corpus checking itself.
            [(ushort)SenkushaMessage.TakionProtocolRequest] = ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ushort)SenkushaMessage.TakionProtocolRequest),
                ProtobufWriter.Message(
                    TakionProtocolRequestField,
                    ProtobufWriter.Varint(VersionField, ClientVersion))),

            // senkusha_send_big: client_version 9, and the three credential fields set to the
            // EMPTY STRING - present and empty, which is not the same as absent, and is what PP418
            // holds against senkusha.c.
            [(ushort)SenkushaMessage.Big] = ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ushort)SenkushaMessage.Big),
                ProtobufWriter.Message(
                    BigPayloadField,
                    ProtobufWriter.Varint(ClientVersionField, ClientVersion),
                    ProtobufWriter.Bytes(SessionKeyField, []),
                    ProtobufWriter.Bytes(LaunchSpecField, []),
                    ProtobufWriter.Bytes(EncryptedKeyField, []))),
        };

    /// <summary>TakionMessage.type.</summary>
    public const int TypeField = 1;

    /// <summary>TakionMessage.big_payload.</summary>
    public const int BigPayloadField = 2;

    /// <summary>TakionMessage.takion_protocol_request.</summary>
    public const int TakionProtocolRequestField = 31;

    /// <summary>TakionProtocolRequestPayload.version.</summary>
    public const int VersionField = 1;

    /// <summary>BigPayload.client_version.</summary>
    public const int ClientVersionField = 1;

    /// <summary>BigPayload.session_key.</summary>
    public const int SessionKeyField = 2;

    /// <summary>BigPayload.launch_spec.</summary>
    public const int LaunchSpecField = 3;

    /// <summary>BigPayload.encrypted_key.</summary>
    public const int EncryptedKeyField = 4;

    /// <summary>The version both of senkusha's messages carry.</summary>
    public const uint ClientVersion = 9;

    /// <summary>
    /// What senkusha says first, which is the version request.
    ///
    /// The state is set to EXPECT_PROTOCOL_ACK before this goes, so the participant is already
    /// waiting on the answer by the time it is sent.
    /// </summary>
    public IReadOnlyList<string> Opening(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel == ChiakiNg.Native.ChiakiMessageTap.SenkushaChannel
            ? [Render(SenkushaMessage.TakionProtocolRequest)]
            : [];
    }

    /// <summary>One thing the console said, and everything this would say back.</summary>
    public IReadOnlyList<string> Receive(string channel, string payload)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        if (channel != ChiakiNg.Native.ChiakiMessageTap.SenkushaChannel)
            return [];

        return TypeOf(payload) switch
        {
            // The version is agreed, so the BIG goes and the wait moves to BANG.
            SenkushaMessage.TakionProtocolRequestAck => Answer(),

            // The handshake is done. Everything after this is the measurement, which this does not
            // model - see the note on the class.
            SenkushaMessage.Bang => Finish(),

            _ => [],
        };
    }

    /// <summary>The type a rendered payload leads with, or null where it has none.</summary>
    public static SenkushaMessage? TypeOf(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return payload.Length >= 4
            && ushort.TryParse(
                payload.AsSpan(0, 4),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out ushort type)
            ? (SenkushaMessage)type
            : null;
    }

    /// <summary>A message as the recording renders one: the type, then its payload bytes.</summary>
    public static string Render(SenkushaMessage message)
        => Payloads.TryGetValue((ushort)message, out byte[]? bytes)
            ? $"{(ushort)message:x4} {string.Join('-', bytes.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)))}"
            : $"{(ushort)message:x4} ";

    private IReadOnlyList<string> Answer()
    {
        VersionAgreed = true;
        return [Render(SenkushaMessage.Big)];
    }

    private IReadOnlyList<string> Finish()
    {
        Finished = true;
        return [];
    }
}

/// <summary>
/// The protobuf payload types senkusha's handshake uses, by the value takion.proto gives them.
/// </summary>
public enum SenkushaMessage : ushort
{
    /// <summary>The client's launch message. Carries no credential on this channel (PP418).</summary>
    Big = 0,

    /// <summary>The console's answer to it. An acknowledgement, not a key exchange.</summary>
    Bang = 1,

    /// <summary>DISCONNECT, which ends the channel.</summary>
    Disconnect = 8,

    /// <summary>The MTU and echo commands. Not modelled - see SenkushaExchangeParticipant.</summary>
    Senkusha = 12,

    /// <summary>The version request senkusha opens with.</summary>
    TakionProtocolRequest = 31,

    /// <summary>And the console agreeing to it.</summary>
    TakionProtocolRequestAck = 32,
}
