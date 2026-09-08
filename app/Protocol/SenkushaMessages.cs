namespace ChiakiNg.Protocol;

/// <summary>One thing senkusha puts on the wire, as its own chokepoint takes it.</summary>
/// <param name="Channel">
/// The takion CHANNEL, which senkusha.c calls <c>data_type</c> and passes as the channel.
///
/// One or eight, and which is not decoration: the version and big messages go on 1, the echo and
/// MTU commands on 8. PP419 found the recording's own column had been emitting this instead of the
/// payload type, so the pair is carried together rather than collapsed.
///
/// PP813: A CHANNEL AND NOT THE CHUNK FLAGS, which is the whole of why senkusha never measured.
/// <c>chiaki_takion_send_message_data(takion, chunk_flags, channel, ...)</c> takes the flags FIRST,
/// and senkusha's chokepoint calls it as <c>(&amp;senkusha->takion, 1, data_type, ...)</c> - so the
/// literal one is the flags and the varying 1-or-8 is the channel. This port had them the other way
/// round, and the console answered exactly as a console would: it acked the datagram, because takion
/// acks by sequence number whatever the channel, and never echoed, because nothing was listening on
/// the channel an echo command was owed. Every message senkusha sends on channel 1 was unaffected -
/// the version request and the BIG both carry a one either way - which is why the bang arrived and
/// only the measurements failed.
/// </param>
/// <param name="PayloadType">TakionMessage.type, which is what the console dispatches on.</param>
/// <param name="Body">The encoded protobuf.</param>
public readonly record struct SenkushaSend(ushort Channel, SenkushaMessage PayloadType, byte[] Body);

/// <summary>
/// PP791, under PP784: the six protobufs senkusha.c sends, which had no managed bytes at all.
///
/// senkusha.c sends through one chokepoint - senkusha_send_data, taking a data type, a payload type
/// and a body - and six callers build what goes through it. PP790's run names each of them as a
/// member of <see cref="ISenkushaRunHost"/> and a host cannot answer one without these.
///
/// THE BYTES ARE WRITTEN RATHER THAN GENERATED, which is <see cref="StreamMessages"/>' shape one
/// module over and the same reason: nanopb encodes a field the generated C# would omit. The C sets
/// the BIG's three strings to "" through chiaki_pb_encode_string, and an empty string is a field
/// that is PRESENT and zero bytes long - a writer that skipped it sends a message the console reads
/// as a different one.
///
/// AND THE VERSION LIST IS UNPACKED. chiaki_pb_encode_list writes a tag before every item, so a
/// single-element repeated uint32 is one tag and one varint and not a length-delimited block. Proto3
/// would pack it; this proto is proto2 and nanopb does not.
///
/// SENKUSHA'S BIG IS NOT THE STREAM'S. Its client_version is nine and its session key, launch spec
/// and encrypted key are all empty - there is nothing to launch, because senkusha measures a link
/// and then disconnects.
/// </summary>
public static class SenkushaMessages
{
    /// <summary>TakionMessage.type, the field every one of these opens with.</summary>
    public const int TypeField = SenkushaExchangeParticipant.TypeField;

    /// <summary>TakionMessage.senkusha_payload.</summary>
    public const int SenkushaPayloadField = 14;

    /// <summary>TakionMessage.disconnect_payload.</summary>
    public const int DisconnectPayloadField = 10;

    /// <summary>DisconnectPayload.reason.</summary>
    public const int ReasonField = 1;

    /// <summary>SenkushaPayload.command.</summary>
    public const int CommandField = 1;

    /// <summary>SenkushaPayload.echo_command.</summary>
    public const int EchoCommandField = 2;

    /// <summary>SenkushaPayload.mtu_command.</summary>
    public const int MtuCommandField = 3;

    /// <summary>SenkushaPayload.client_mtu_command.</summary>
    public const int ClientMtuCommandField = 5;

    /// <summary>SenkushaEchoCommand.state.</summary>
    public const int EchoStateField = 1;

    /// <summary>SenkushaMtuCommand.id, and SenkushaClientMtuCommand.id.</summary>
    public const int IdField = 1;

    /// <summary>SenkushaMtuCommand.mtu_req, and the client command's.</summary>
    public const int MtuReqField = 2;

    /// <summary>SenkushaMtuCommand.num.</summary>
    public const int NumField = 4;

    /// <summary>SenkushaClientMtuCommand.state, which is field three and not the echo's one.</summary>
    public const int ClientStateField = 3;

    /// <summary>SenkushaClientMtuCommand.mtu_down.</summary>
    public const int MtuDownField = 4;

    /// <summary>TakionMessage.PayloadType.BIG, from the enum the exchange already names.</summary>
    public const SenkushaMessage BigType = SenkushaMessage.Big;

    /// <summary>TakionMessage.PayloadType.DISCONNECT.</summary>
    public const SenkushaMessage DisconnectType = SenkushaMessage.Disconnect;

    /// <summary>TakionMessage.PayloadType.SENKUSHA, which four of the six go under.</summary>
    public const SenkushaMessage SenkushaType = SenkushaMessage.Senkusha;

    /// <summary>TakionMessage.PayloadType.TAKIONPROTOCOLREQUEST.</summary>
    public const SenkushaMessage ProtocolRequestType = SenkushaMessage.TakionProtocolRequest;

    /// <summary>SenkushaPayload.Command.ECHO_COMMAND.</summary>
    public const int EchoCommand = 0;

    /// <summary>SenkushaPayload.Command.MTU_COMMAND.</summary>
    public const int MtuCommand = 1;

    /// <summary>SenkushaPayload.Command.CLIENT_MTU_COMMAND, which is four and not two.</summary>
    public const int ClientMtuCommand = 4;

    /// <summary>The channel the version and big messages go out on.</summary>
    public const ushort OrdinaryChannel = 1;

    /// <summary>And the one the echo and MTU commands go out on, which is what an ack is waited on.</summary>
    public const ushort CommandChannel = 8;

    /// <summary>
    /// The chunk flags every senkusha send carries, which is one and never varies.
    ///
    /// PP813: the literal the C's chokepoint passes in the argument BEFORE the channel. It is also
    /// what <see cref="TakionDataPush.ExpectedTypeB"/> says a reader warns about anything else for,
    /// so the two agree by construction rather than by coincidence.
    /// </summary>
    public const byte ChunkFlags = TakionDataPush.ExpectedTypeB;

    /// <summary>The takion version this side asks for, which is nine even on senkusha's own seven.</summary>
    public const uint SupportedVersion = 9;

    /// <summary>BigPayload.client_version, likewise.</summary>
    public const uint BigClientVersion = 9;

    /// <summary>What the disconnect says, byte for byte.</summary>
    public const string DisconnectReason = "Client Disconnecting";

    /// <summary>
    /// senkusha_set_version: one supported takion version, in an unpacked repeated field.
    /// </summary>
    public static SenkushaSend SetVersion()
        => new(
            OrdinaryChannel,
            ProtocolRequestType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ulong)ProtocolRequestType),
                ProtobufWriter.Message(
                    SenkushaExchangeParticipant.TakionProtocolRequestField,
                    ProtobufWriter.Varint(SenkushaExchangeParticipant.VersionField, SupportedVersion))));

    /// <summary>
    /// senkusha_send_big, which carries a version and three empty strings.
    ///
    /// The three are PRESENT and empty, not absent. There is nothing to launch here - senkusha
    /// measures and disconnects - and the console still expects the fields.
    /// </summary>
    public static SenkushaSend Big()
        => new(
            OrdinaryChannel,
            BigType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ulong)BigType),
                ProtobufWriter.Message(
                    SenkushaExchangeParticipant.BigPayloadField,
                    ProtobufWriter.Varint(SenkushaExchangeParticipant.ClientVersionField, BigClientVersion),
                    ProtobufWriter.Bytes(SenkushaExchangeParticipant.SessionKeyField, []),
                    ProtobufWriter.Bytes(SenkushaExchangeParticipant.LaunchSpecField, []),
                    ProtobufWriter.Bytes(SenkushaExchangeParticipant.EncryptedKeyField, []))));

    /// <summary>senkusha_send_disconnect, from the label that tells the console.</summary>
    public static SenkushaSend Disconnect()
        => new(
            OrdinaryChannel,
            DisconnectType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ulong)DisconnectType),
                ProtobufWriter.Message(
                    DisconnectPayloadField,
                    ProtobufWriter.Bytes(
                        ReasonField, System.Text.Encoding.ASCII.GetBytes(DisconnectReason)))));

    /// <summary>
    /// senkusha_send_echo_command, which the round-trip test brackets its ten pings with.
    ///
    /// Sent on the channel an ack is waited on, because the C waits for one either side of the
    /// test - and a run that sent the pings before the console started echoing measures nothing.
    /// </summary>
    public static SenkushaSend EchoCommandMessage(bool enable)
        => new(
            CommandChannel,
            SenkushaType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ulong)SenkushaType),
                ProtobufWriter.Message(
                    SenkushaPayloadField,
                    ProtobufWriter.Varint(CommandField, EchoCommand),
                    ProtobufWriter.Message(
                        EchoCommandField, ProtobufWriter.Bool(EchoStateField, enable)))));

    /// <summary>
    /// senkusha_send_mtu_command: the inbound search's probe, asking the console to send one back.
    /// </summary>
    /// <param name="id">The request id, which the response has to carry in its frame index.</param>
    /// <param name="mtuReq">The size under test.</param>
    /// <param name="num">How many the console should send, which the C always sets to one.</param>
    public static SenkushaSend MtuCommandMessage(uint id, uint mtuReq, uint num = 1)
        => new(
            CommandChannel,
            SenkushaType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ulong)SenkushaType),
                ProtobufWriter.Message(
                    SenkushaPayloadField,
                    ProtobufWriter.Varint(CommandField, MtuCommand),
                    ProtobufWriter.Message(
                        MtuCommandField,
                        ProtobufWriter.Varint(IdField, id),
                        ProtobufWriter.Varint(MtuReqField, mtuReq),
                        ProtobufWriter.Varint(NumField, num)))));

    /// <summary>
    /// senkusha_send_client_mtu_command: what the outbound search opens and closes with.
    ///
    /// The two sends differ in every field. The opening one is id one, state TRUE and the inbound
    /// answer in both size fields; the closing one is id two, state FALSE, and mtu_req is the
    /// window's CEILING - the size that stopped carrying, not the one that does, which is PP789's
    /// finding and is reported as it stands.
    /// </summary>
    public static SenkushaSend ClientMtuCommandMessage(uint id, uint mtuReq, bool state, uint mtuDown)
        => new(
            CommandChannel,
            SenkushaType,
            ProtobufWriter.Concat(
                ProtobufWriter.Varint(TypeField, (ulong)SenkushaType),
                ProtobufWriter.Message(
                    SenkushaPayloadField,
                    ProtobufWriter.Varint(CommandField, ClientMtuCommand),
                    ProtobufWriter.Message(
                        ClientMtuCommandField,
                        ProtobufWriter.Varint(IdField, id),
                        ProtobufWriter.Varint(MtuReqField, mtuReq),
                        ProtobufWriter.Bool(ClientStateField, state),
                        ProtobufWriter.Varint(MtuDownField, mtuDown)))));
}
