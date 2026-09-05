using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP727, under PP707: stream_connection_send_big's payload - the message that asks for a stream.
///
/// The last of the four subsystems PP712's census owed the run's host. PP726 formats the launch
/// spec; this obfuscates it, base64s it, and puts it in a BIG beside the session key, a zeroed
/// encrypted key and the ECDH public key with its signature.
///
/// THE OBFUSCATION IS NOT AN ENCRYPTION OF THE SPEC, and the three lines it takes read as though it
/// were. The C zeroes a buffer, encrypts THAT at counter zero, and XORs the result with the JSON.
/// rpcrypt is AES-128-CFB128, where a block's key stream is the encryption of the previous
/// CIPHERTEXT block - so encrypting zeros yields E(iv), E(E(iv)), E(E(E(iv))), which is the OFB key
/// stream, and XORing it with the plaintext is AES-OFB. A port that called rpcrypt on the JSON
/// instead would agree for one block and differ in every byte after it, with no symptom but a
/// console that never sends BANG.
///
/// AND THE TERMINATOR IS PART OF THE MESSAGE. The size is incremented by one after the format -
/// "we also want the trailing 0" - so the NUL is obfuscated and encoded with the rest.
/// <see cref="SpecBytes"/> is where that one byte is added, on its own, because it is the sort of
/// thing a reader assumes away.
///
/// THE ENCRYPTED KEY IS FOUR ZERO BYTES, not an absent field and not an empty one. It is `required`
/// in the proto, and the C has a callback whose whole body writes { 0, 0, 0, 0 }.
///
/// THIS IS NOT ONE OF <see cref="StreamMessages"/>' SEVEN. Those go through
/// stream_connection_send_data; the BIG is encoded whole, tapped once, and then cut into MTU-sized
/// fragments by <see cref="BigFragments"/> - which is why it carries no takion data type here.
/// </summary>
public static class BigMessage
{
    /// <summary>tkproto_TakionMessage_PayloadType_BIG, which is zero and so is easy to lose.</summary>
    public const ushort BigType = 0;

    /// <summary>TakionMessage.big_payload.</summary>
    public const int BigPayloadField = 2;

    /// <summary>BigPayload.client_version - takion's negotiated version, not the app's.</summary>
    public const int ClientVersionField = 1;

    /// <summary>BigPayload.session_key, which carries session->session_id as a string.</summary>
    public const int SessionKeyField = 2;

    /// <summary>BigPayload.launch_spec, the base64 of the obfuscated JSON.</summary>
    public const int LaunchSpecField = 3;

    /// <summary>BigPayload.encrypted_key.</summary>
    public const int EncryptedKeyField = 4;

    /// <summary>BigPayload.ecdh_pub_key.</summary>
    public const int EcdhPubKeyField = 5;

    /// <summary>BigPayload.ecdh_sig.</summary>
    public const int EcdhSigField = 6;

    /// <summary>The counter the launch spec is obfuscated at. Always this one.</summary>
    public const ulong LaunchSpecCounter = 0;

    /// <summary>
    /// chiaki_pb_encode_zero_encrypted_key: four zero bytes.
    ///
    /// A field that is present and says nothing, rather than one that is absent. The proto marks it
    /// required, so a port omitting it writes a message nanopb refuses on the console's side.
    /// </summary>
    public static ReadOnlySpan<byte> ZeroEncryptedKey => [0, 0, 0, 0];

    /// <summary>
    /// The bytes the spec is obfuscated over: the JSON and its terminator.
    /// </summary>
    /// <remarks>
    /// The one byte is the whole of this function, which is why it is one. `launch_spec_json_size
    /// += 1` sits between the format and the encrypt with the comment "we also want the trailing
    /// 0", and a port encoding the string's own length sends a spec one byte short.
    /// </remarks>
    public static byte[] SpecBytes(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(json) + 1];
        Encoding.UTF8.GetBytes(json, bytes);

        return bytes;
    }

    /// <summary>
    /// The key stream: rpcrypt over a buffer of zeros, at counter zero.
    ///
    /// Under CFB this is E(iv), E(E(iv)), E(E(E(iv))) and so on - the OFB key stream, because with
    /// a zero plaintext the ciphertext fed back IS the key stream. The C reaches it by encrypting
    /// in place over a zeroed buffer, which is the same thing said less directly.
    /// </summary>
    public static byte[] KeyStream(RpCrypt crypt, int length)
    {
        ArgumentNullException.ThrowIfNull(crypt);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        return crypt.Encrypt(LaunchSpecCounter, new byte[length]);
    }

    /// <summary>xor_bytes: the second half of the obfuscation, once the key stream is in hand.</summary>
    public static byte[] Obfuscate(ReadOnlySpan<byte> keyStream, ReadOnlySpan<byte> plain)
    {
        if (keyStream.Length != plain.Length)
            throw new ArgumentException("the key stream is as long as what it hides", nameof(keyStream));

        byte[] hidden = new byte[plain.Length];
        for (var at = 0; at < plain.Length; at++)
            hidden[at] = (byte)(keyStream[at] ^ plain[at]);

        return hidden;
    }

    /// <summary>
    /// The launch_spec field: PP726's JSON, obfuscated and base64'd.
    /// </summary>
    /// <param name="crypt">The session's rpcrypt.</param>
    /// <param name="fields">What the spec states.</param>
    /// <param name="handshakeKey">The handshake key, which goes into the spec encoded.</param>
    /// <returns>The field's text, or null where the spec would not fit the C's buffer.</returns>
    public static string? EncodedLaunchSpec(
        RpCrypt crypt, LaunchSpecFields fields, ReadOnlySpan<byte> handshakeKey)
    {
        ArgumentNullException.ThrowIfNull(crypt);

        if (LaunchSpec.Format(fields, handshakeKey) is not { } json)
            return null;

        byte[] plain = SpecBytes(json);

        return Convert.ToBase64String(Obfuscate(KeyStream(crypt, plain.Length), plain));
    }

    /// <summary>
    /// The whole message, encoded - the bytes PP395's tap emits and PP376's loop then cuts up.
    /// </summary>
    /// <param name="clientVersion">stream_connection->takion.version.</param>
    /// <param name="sessionKey">session->session_id, written as its bytes up to the terminator.</param>
    /// <param name="encodedLaunchSpec"><see cref="EncodedLaunchSpec"/>.</param>
    /// <param name="ecdhPubKey">The local public key, which OpenSSL produces behind the seam.</param>
    /// <param name="ecdhSig">Its signature under the handshake key.</param>
    public static byte[] Encode(
        uint clientVersion,
        string sessionKey,
        string encodedLaunchSpec,
        ReadOnlySpan<byte> ecdhPubKey,
        ReadOnlySpan<byte> ecdhSig)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentNullException.ThrowIfNull(encodedLaunchSpec);

        return ProtobufWriter.Concat(
            ProtobufWriter.Varint(StreamMessages.TypeField, BigType),
            ProtobufWriter.Message(
                BigPayloadField,
                ProtobufWriter.Varint(ClientVersionField, clientVersion),
                ProtobufWriter.Bytes(SessionKeyField, Encoding.UTF8.GetBytes(sessionKey)),
                ProtobufWriter.Bytes(LaunchSpecField, Encoding.UTF8.GetBytes(encodedLaunchSpec)),
                ProtobufWriter.Bytes(EncryptedKeyField, ZeroEncryptedKey),
                ProtobufWriter.Bytes(EcdhPubKeyField, ecdhPubKey),
                ProtobufWriter.Bytes(EcdhSigField, ecdhSig)));
    }
}

/// <summary>
/// PP727: the four decisions inside send_big, read out of streamconnection.c.
///
/// None of them is reachable from a test - the function is static and takes a stream connection
/// holding a live takion - so what holds this port to it is the file's own text, as PP726's
/// template is held against launchspec.c.
/// </summary>
public static class BigMessageSource
{
    /// <summary>Where the sender is.</summary>
    public const string RelativePath = StreamDispatchSource.RelativePath;

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The sender's body, or null where it is gone.</summary>
    public static string? SendBody(string source)
        => CFunction.Body(source, "static ChiakiErrorCode stream_connection_send_big(");

    /// <summary>
    /// Whether the spec is still hidden by encrypting zeros and XORing, rather than by encrypting it.
    ///
    /// Three things in one order: the buffer is zeroed, rpcrypt runs over it in place, and the JSON
    /// is XORed into the result. Collapsing them into one encrypt changes the cipher mode from OFB
    /// to CFB, which is a different message from the second block on.
    /// </summary>
    public static bool TheSpecIsStillHiddenByAKeyStreamAndNotEncrypted(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        int zeroed = sendBody.IndexOf("memset(launch_spec_json_enc, 0,", StringComparison.Ordinal);
        if (zeroed < 0)
            return false;

        int encrypted = sendBody.IndexOf(
            "chiaki_rpcrypt_encrypt(&session->rpcrypt, 0, launch_spec_json_enc, launch_spec_json_enc",
            zeroed,
            StringComparison.Ordinal);

        int xored = sendBody.IndexOf("xor_bytes(launch_spec_json_enc,", encrypted < 0 ? zeroed : encrypted,
            StringComparison.Ordinal);

        return encrypted > zeroed && xored > encrypted;
    }

    /// <summary>
    /// Whether the terminator is still counted in, before any of that runs.
    ///
    /// One increment, with a comment saying why. It has to happen before the encrypt, or the NUL is
    /// outside the obfuscated range and the spec on the wire is a byte short.
    /// </summary>
    public static bool TheTerminatorIsStillCountedIn(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        int counted = sendBody.IndexOf("launch_spec_json_size += 1;", StringComparison.Ordinal);
        if (counted < 0)
            return false;

        return sendBody.IndexOf("chiaki_rpcrypt_encrypt(", counted, StringComparison.Ordinal) > counted;
    }

    /// <summary>
    /// Whether the encrypted key is still four zero bytes written by a callback of its own.
    ///
    /// The whole body of chiaki_pb_encode_zero_encrypted_key. An empty field would be a shorter
    /// message the console reads differently, and an absent one is refused: the proto says required.
    /// </summary>
    public static bool TheEncryptedKeyIsStillFourZeroBytes(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, "static bool chiaki_pb_encode_zero_encrypted_key(");

        return body is not null
            && body.Contains("uint8_t data[] = { 0, 0, 0, 0 };", StringComparison.Ordinal)
            && body.Contains("pb_encode_string(stream, data, sizeof(data));", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the BIG still bypasses the chokepoint every other message goes through.
    ///
    /// PP684's seven all call stream_connection_send_data; this one encodes whole and hands the
    /// buffer to takion in fragments. A port that routed it through the same builder would give it
    /// a data type it does not have and lose the fragmentation PP376 holds.
    /// </summary>
    public static bool TheBigStillBypassesTheChokepoint(string sendBody)
    {
        ArgumentNullException.ThrowIfNull(sendBody);

        return !sendBody.Contains("stream_connection_send_data(", StringComparison.Ordinal)
            && sendBody.Contains("chiaki_takion_send_message_data(", StringComparison.Ordinal);
    }
}
