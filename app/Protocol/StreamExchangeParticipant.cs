using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What the BANG said, once one has arrived.</summary>
/// <param name="VersionAccepted">bang_payload.version_accepted, checked first.</param>
/// <param name="EncryptedKeyAccepted">bang_payload.encrypted_key_accepted, checked second.</param>
/// <param name="PublicKeyBytes">
/// The length of ecdh_pub_key. PP423 zeroes the VALUE and keeps the length, so this is askable and
/// what it holds is not.
/// </param>
/// <param name="SignatureBytes">And of ecdh_sig, for the same reason.</param>
public readonly record struct BangVerdict(
    bool VersionAccepted, bool EncryptedKeyAccepted, int PublicKeyBytes, int SignatureBytes)
{
    /// <summary>
    /// Whether all four of the handshake's checks pass, in the order streamconnection.c makes them.
    /// </summary>
    public bool Accepted =>
        VersionAccepted && EncryptedKeyAccepted && PublicKeyBytes > 0 && SignatureBytes > 0;
}

/// <summary>
/// PP424, under PP23: the stream connection's handshake, replayed as a conversation.
///
/// The last of PP23's four channels. PP391 gave ctrl a participant, PP392 session, PP421 senkusha.
///
/// THE BANG'S LADDER IS FOUR CHECKS AND PP423 MADE ALL FOUR READABLE. streamconnection.c tests
/// version_accepted, then encrypted_key_accepted, then that ecdh_pub_key and ecdh_sig are present at
/// all. PP423 zeroed the key VALUES and kept their tags and lengths, so a replay exercises "the
/// console sent a key of this size" while never seeing one. A whole-payload marker would have hidden
/// the two flags and made the presence checks unaskable.
///
/// THE DERIVATION IS THE BOUNDARY. <c>chiaki_ecdh_derive_secret</c> verifies a signature over the
/// key; with both zeroed there is nothing to verify and nothing to compare. So this climbs the four
/// checks and stops, the way <see cref="SenkushaExchangeParticipant"/> stops before the MTU
/// measurement.
///
/// THE BIG IS THE MARKER AND NOT A MESSAGE. It is redacted whole - five of its six fields are secret
/// - so building one would produce a value the comparison cannot see. PP392's session participant
/// builds its request and redacts it, which works because the session channel redacts by FIELD; this
/// one cannot, and says so rather than pretending.
///
/// THE STREAMINFO IS ANSWERED WITH THREE, IN ORDER: the ack, the controller connection, then the
/// microphone's own STREAMINFO. The third is built from <see cref="AudioHeaderArguments.Microphone"/>
/// rather than spelled out, so PP422's correction and this share one source of truth - a re-swap
/// would move both and the corpus would not, which is what turns the replay red.
///
/// THE DISCONNECT IS NOT AN ANSWER. It goes at teardown rather than in reply to the console's ack,
/// so a replay ends at that ack.
/// </summary>
/// <param name="dualSense">
/// connect_info.enable_dualsense, which is the only thing in this handshake that varies: the
/// controller connection announces DUALSENSE or DUALSHOCK4 by it.
/// </param>
public sealed class StreamExchangeParticipant(bool dualSense = false) : IExchangeParticipant
{
    /// <summary>What the BANG said, or null until one arrives.</summary>
    public BangVerdict? Verdict { get; private set; }

    /// <summary>Whether the console's STREAMINFO has been answered.</summary>
    public bool StreamInfoAnswered { get; private set; }

    /// <summary>Whether the console acknowledged this side's microphone STREAMINFO.</summary>
    public bool MicrophoneAcknowledged { get; private set; }

    /// <summary>tkproto_ControllerConnectionPayload_ControllerType_DUALSHOCK4.</summary>
    public const byte DualShock4 = 2;

    /// <summary>And DUALSENSE.</summary>
    public const byte DualSense = 6;

    /// <summary>
    /// The three payloads this sends, as streamconnection.c encodes them.
    ///
    /// Each group named by the field it is, because a byte wrong here is a message the console reads
    /// differently and the comment is what makes that reviewable.
    /// </summary>
    public static byte[] StreamInfoAck() =>
        // 08 0e   type = STREAMINFOACK. The whole message: an ack carries nothing.
        [0x08, 0x0e];

    /// <summary>
    /// The controller connection, which is the one message here that depends on a setting.
    /// </summary>
    public static byte[] ControllerConnection(bool dualSense) =>
    [
        0x08, 0x15,                     // type = CONTROLLERCONNECTION
        0xb2, 0x01, 0x04,               // field 22, 4 bytes - controller_connection_payload
            0x10, 0x01,                 //   field 2 varint 1  - connected = true
            0x18, dualSense ? DualSense : DualShock4,  // field 3 - controller_type
    ];

    /// <summary>
    /// The microphone's STREAMINFO, wrapped round the header PP422 corrected.
    ///
    /// Derived rather than spelled out: the fourteen header bytes come from the same place the
    /// library's do, so the two cannot drift apart without this replay noticing.
    /// </summary>
    public static byte[] MicrophoneStreamInfo()
    {
        byte[] header = AudioHeaderArguments.Microphone();

        return
        [
            0x08, 0x0d,                                   // type = STREAMINFO
            0x7a, (byte)(header.Length + 2),               // field 15 - stream_info_payload
                0x12, (byte)header.Length,                //   field 2 - audio_header
                .. header,
        ];
    }

    /// <summary>What the client says first, which is the BIG.</summary>
    public IReadOnlyList<string> Opening(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // The marker, not a message. See the note on the class.
        return channel == ChiakiMessageTap.StreamChannel
            ? [$"{MessageSecrets.StreamSecret["BIG"]:x4} {MessageSecrets.Marker}"]
            : [];
    }

    /// <summary>One thing the console said, and everything this would say back.</summary>
    public IReadOnlyList<string> Receive(string channel, string payload)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        if (channel != ChiakiMessageTap.StreamChannel)
            return [];

        ushort type = ExchangeCorpus.TypeIn(
            new ExchangeEntry(0, ExchangeDirection.Received, channel, payload));

        if (type == MessageSecrets.StreamSecret["BANG"])
        {
            Verdict = ReadVerdict(payload);

            // Nothing goes back. The client derives its keys and waits for the STREAMINFO.
            return [];
        }

        if (type == StreamInfo)
        {
            StreamInfoAnswered = true;
            return
            [
                Render(StreamInfoAck()),
                Render(ControllerConnection(dualSense)),
                Render(MicrophoneStreamInfo()),
            ];
        }

        if (type == StreamInfoAckType)
        {
            MicrophoneAcknowledged = true;
            return [];
        }

        return [];
    }

    /// <summary>tkproto_TakionMessage_PayloadType_STREAMINFO.</summary>
    public const ushort StreamInfo = 13;

    /// <summary>And its ack, which crosses both ways.</summary>
    public const ushort StreamInfoAckType = 14;

    /// <summary>
    /// The four things the BANG's ladder asks, read out of a recorded one.
    ///
    /// Absent optional fields read as false and zero, which is what the C sees too: a BANG without
    /// ecdh_pub_key fails its presence check rather than being treated as having an empty one.
    /// </summary>
    public static BangVerdict ReadVerdict(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte[] bytes = BytesIn(payload);

        if (!ProtobufRedaction.TryFindField(
                bytes, 0, bytes.Length, MessageSecrets.BangPayloadField,
                out int at, out int length))
        {
            return new BangVerdict(false, false, 0, 0);
        }

        return new BangVerdict(
            VersionAccepted: FlagAt(bytes, at, length, VersionAcceptedField),
            EncryptedKeyAccepted: FlagAt(bytes, at, length, EncryptedKeyAcceptedField),
            PublicKeyBytes: LengthAt(bytes, at, length, PublicKeyField),
            SignatureBytes: LengthAt(bytes, at, length, SignatureField));
    }

    /// <summary>bang_payload.encrypted_key_accepted.</summary>
    public const int EncryptedKeyAcceptedField = 3;

    /// <summary>bang_payload.version_accepted.</summary>
    public const int VersionAcceptedField = 4;

    /// <summary>bang_payload.ecdh_pub_key.</summary>
    public const int PublicKeyField = 8;

    /// <summary>bang_payload.ecdh_sig.</summary>
    public const int SignatureField = 9;

    private static bool FlagAt(byte[] bytes, int from, int length, int field)
        => ProtobufRedaction.TryFindField(bytes, from, from + length, field, out int at, out int width)
            && width > 0
            && bytes[at] != 0;

    private static int LengthAt(byte[] bytes, int from, int length, int field)
        => ProtobufRedaction.TryFindField(bytes, from, from + length, field, out _, out int width)
            ? width
            : 0;

    /// <summary>A message as the recording renders one.</summary>
    public static string Render(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return $"{(payload.Length >= 2 ? payload[1] : 0):x4} "
            + string.Join(
                '-',
                payload.Select(
                    b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static byte[] BytesIn(string payload)
        => payload.Length <= 5 || payload.Contains('<', StringComparison.Ordinal)
            ? []
            : [.. payload[5..]
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => Convert.ToByte(pair, 16))];
}
