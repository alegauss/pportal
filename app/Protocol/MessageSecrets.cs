using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP397: which payloads never reach a recording, asked of the CHANNEL as well as the type.
///
/// PP326 settled this for the control channel and keyed it to the message type - SESSION_ID goes
/// because its payload IS the session id, LOGIN_PIN_REP because it is the PIN the user typed. The
/// recorder consulted that list for every channel that is not the session one, and for a long time
/// there was only one such channel.
///
/// PP394 AND PP395 ADDED TWO, AND NEITHER NUMBERS ITS MESSAGES THE SAME WAY. A ctrl message carries
/// 0x33 or 0x8004; a stream or senkusha message carries a protobuf payload type, 0 to 25. One list
/// consulted across three numbering schemes is a rule that means something different in each.
///
/// THE LEAK WAS REAL AND IN ONE DIRECTION. stream_connection_send_big sets
/// <c>msg.big_payload.session_key.arg = session-&gt;session_id</c>, so a BIG carries the session id -
/// the very value PP326 redacts one channel over. Nothing in a ctrl-keyed list could name it, and
/// the corpus is a file in a public repository.
///
/// THE TAKION DATA TYPE COULD NOT HAVE FIXED IT. Five different messages cross the stream channel
/// as data type 1 - BIG, BANG, heartbeat, disconnect, controller connection - so that number
/// identifies nothing a rule could name. The tap carries the protobuf payload type instead, which
/// is the discriminator the wire actually has.
///
/// AND A MESSAGE THAT WOULD NOT DECODE IS REFUSED. PP326's principle is that a value goes because
/// of the field it sits in; with no field identified there is no basis to record it, so
/// <see cref="ChiakiMessageTap.UnknownType"/> is never recordable.
/// </summary>
public static class MessageSecrets
{
    /// <summary>What replaces a secret payload. PP326's marker, unchanged.</summary>
    public const string Marker = CtrlMessageSecrets.Marker;

    /// <summary>
    /// The stream channel's secret-bearing messages, by protobuf payload type.
    ///
    /// BIG carries the session id, the launch spec and the encrypted key. BANG carries the console's
    /// ECDH public key and its signature, which are what the session keys are derived from.
    ///
    /// STREAMINFO IS NOT HERE and that is a decision rather than an omission: it carries the audio
    /// and video headers, which are the thing PP372 hands to a decoder and exactly what a replay of
    /// this channel is for. Redacting it would buy nothing and cost the oracle.
    /// </summary>
    public static IReadOnlyDictionary<string, ushort> StreamSecret { get; } =
        new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            // tkproto_TakionMessage_PayloadType_BIG
            ["BIG"] = 0,

            // tkproto_TakionMessage_PayloadType_BANG
            ["BANG"] = 1,
        };

    /// <summary>
    /// And senkusha's, which is nothing.
    ///
    /// Its BIG sets session_key, launch_spec and encrypted_key to the empty string - senkusha
    /// measures a link and carries no credential - and the rest of its traffic is MTU sizes and
    /// echo commands. Stated as an empty set rather than left out, so the answer is recorded.
    /// </summary>
    public static IReadOnlySet<ushort> SenkushaSecret { get; } = new HashSet<ushort>();

    /// <summary>
    /// Whether a message of this type on this channel may have its payload recorded.
    /// </summary>
    /// <param name="channel">Which conversation it crossed.</param>
    /// <param name="type">A ctrl message type, or a protobuf payload type on the other two.</param>
    public static bool MayRecord(string channel, ushort type)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // A message nothing could classify is not recorded, on any channel.
        if (type == ChiakiMessageTap.UnknownType)
            return false;

        if (channel == ChiakiMessageTap.CtrlChannel)
            return CtrlMessageSecrets.MayRecord(type);

        if (channel == ChiakiMessageTap.StreamChannel)
            return !StreamSecret.Values.Contains(type);

        if (channel == ChiakiMessageTap.SenkushaChannel)
            return !SenkushaSecret.Contains(type);

        // The session channel is an HTTP head and is redacted by field, not by type (PP325).
        return true;
    }
}
