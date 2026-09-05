using ChiakiNg.Session;
using Google.Protobuf;

namespace ChiakiNg.Protocol;

/// <summary>What one message arriving in EXPECT_BANG came to.</summary>
public enum BangOutcome
{
    /// <summary>The protobuf would not decode. Logged, and NEITHER flag is written.</summary>
    Undecodable,

    /// <summary>A DISCONNECT, which its own handler takes from here. Neither flag.</summary>
    ToDisconnect,

    /// <summary>A STREAMINFO arriving before the state that wants it, kept for the replay.</summary>
    SavedEarly,

    /// <summary>Anything else - including a SECOND early streaminfo. Warned, hexdumped, dropped.</summary>
    Unexpected,

    /// <summary>A bang the console refused, or one this side could not key from. state_failed.</summary>
    Refused,

    /// <summary>A bang accepted and keyed. state_finished, which is what ends the wait.</summary>
    Keyed,
}

/// <summary>Why a bang was refused, in the order the C tests them.</summary>
public enum BangRefusal
{
    /// <summary>The console did not accept the client's version.</summary>
    VersionNotAccepted,

    /// <summary>It did not accept the encrypted key - the four zero bytes PP727 sends.</summary>
    EncryptedKeyNotAccepted,

    /// <summary>No remote ECDH public key came with it.</summary>
    NoEcdhPubKey,

    /// <summary>No signature over that key came with it.</summary>
    NoEcdhSig,

    /// <summary>chiaki_ecdh_derive_secret refused the pair.</summary>
    DeriveFailed,

    /// <summary>The secret came out and the gk crypt would not start from it.</summary>
    CryptFailed,
}

/// <summary>What the handler decided, and what the flags read afterwards.</summary>
/// <param name="Outcome">Which of the six.</param>
/// <param name="Refusal">Why, where the outcome is a refusal; null otherwise.</param>
/// <param name="Flags">The two flags as the handler leaves them, for the run to read.</param>
public readonly record struct BangReading(
    BangOutcome Outcome, BangRefusal? Refusal, StreamWaitState Flags);

/// <summary>
/// The keying a bang leads to, which is OpenSSL's and stays behind the seam.
/// </summary>
public interface IBangKeying
{
    /// <summary>chiaki_ecdh_derive_secret, over the console's key and its signature.</summary>
    bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig);

    /// <summary>stream_connection_init_crypt, which only runs where a secret came out.</summary>
    bool InitCrypt();
}

/// <summary>
/// PP729, under PP707: stream_connection_takion_data_expect_bang - where a session becomes keyed.
///
/// PP721 wired PP366's second dispatch layer. The third routes a protobuf by STATE to one of three
/// handlers, and this was the one with no port: PP686 answers for the streaminfo and PP684 for the
/// idle arm, and the bang had four scalars read by a decoder and nothing that decided on them.
///
/// TWO KINDS OF NOT-A-BANG, AND ONLY ONE OF THEM FAILS ANYTHING. A message that will not decode, a
/// disconnect, an early streaminfo and an unrecognised type all RETURN, leaving both flags as they
/// were - so the run's wait carries on and the console gets its whole timeout. A bang that IS one
/// and is refused sets state_failed. The difference is the behaviour: a console answering something
/// unexpected does not end the state, and a console saying no does.
///
/// EXCEPT THAT state_failed IS READ BY NOBODY, which PP365 established and
/// <see cref="StreamConnectionStates.WaitEnds"/> holds. So a refused bang and a silent console
/// arrive at the run identically, one of them after the full wait. Reproduced rather than repaired:
/// a port that ended the wait on the flag would report failures sooner than the C, which is better
/// behaviour and different behaviour.
///
/// A SECOND EARLY STREAMINFO IS DROPPED. The save is guarded on the buffer being empty and the
/// guard's body returns; a second one falls out of the arm into the warning below it. So the FIRST
/// early streaminfo is the one replayed, and the C never says it discarded another.
/// </summary>
public static class BangHandler
{
    /// <summary>The C's ecdh_pub_key buffer. A field longer than this fails the whole decode.</summary>
    public const int EcdhPubKeyMax = 128;

    /// <summary>And the signature's, which is four times smaller.</summary>
    public const int EcdhSigMax = 32;

    /// <summary>
    /// One message, in the state that is waiting for a bang.
    /// </summary>
    /// <param name="payload">The protobuf, as the data layer handed it over.</param>
    /// <param name="earlyStreaminfoHeld">Whether a streaminfo has already been saved.</param>
    /// <param name="keying">Where the derivation happens.</param>
    public static BangReading Read(
        ReadOnlySpan<byte> payload, bool earlyStreaminfoHeld, IBangKeying keying)
    {
        ArgumentNullException.ThrowIfNull(keying);

        Tkproto.TakionMessage message;
        try
        {
            message = Tkproto.TakionMessage.Parser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException)
        {
            return Nothing(BangOutcome.Undecodable);
        }

        if (message.Type != Tkproto.TakionMessage.Types.PayloadType.Bang || message.BangPayload is null)
            return Nothing(NotABang(message.Type, earlyStreaminfoHeld));

        // The C's own bound, reproduced where nanopb applies it: a field over the buffer's size
        // fails the DECODE, which is the undecodable arm and not a refusal.
        if (message.BangPayload.EcdhPubKey.Length > EcdhPubKeyMax
            || message.BangPayload.EcdhSig.Length > EcdhSigMax)
        {
            return Nothing(BangOutcome.Undecodable);
        }

        if (Refuse(message.BangPayload) is { } why)
            return Failed(why);

        if (!keying.DeriveSecret(
                message.BangPayload.EcdhPubKey.Span, message.BangPayload.EcdhSig.Span))
        {
            return Failed(BangRefusal.DeriveFailed);
        }

        return !keying.InitCrypt()
            ? Failed(BangRefusal.CryptFailed)
            : new BangReading(BangOutcome.Keyed, null, new StreamWaitState(Finished: true));
    }

    /// <summary>
    /// Which of the four refusals a bang payload earns, in the C's order, or null for none.
    ///
    /// The ORDER is stated because the logs differ: a console that accepted neither the version nor
    /// the key is reported as the version, and a reader of that log is looking for the wrong thing
    /// if a port tested them the other way round.
    /// </summary>
    public static BangRefusal? Refuse(Tkproto.BangPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.VersionAccepted)
            return BangRefusal.VersionNotAccepted;

        if (!payload.EncryptedKeyAccepted)
            return BangRefusal.EncryptedKeyNotAccepted;

        if (payload.EcdhPubKey.Length == 0)
            return BangRefusal.NoEcdhPubKey;

        return payload.EcdhSig.Length == 0 ? BangRefusal.NoEcdhSig : null;
    }

    /// <summary>
    /// What a message that is not a bang is, given whether a streaminfo is already saved.
    /// </summary>
    public static BangOutcome NotABang(
        Tkproto.TakionMessage.Types.PayloadType type, bool earlyStreaminfoHeld) => type switch
        {
            Tkproto.TakionMessage.Types.PayloadType.Disconnect => BangOutcome.ToDisconnect,
            Tkproto.TakionMessage.Types.PayloadType.Streaminfo when !earlyStreaminfoHeld
                => BangOutcome.SavedEarly,
            _ => BangOutcome.Unexpected,
        };

    /// <summary>Whether an outcome ends the state's wait, which only one of the six does.</summary>
    public static bool EndsTheWait(BangOutcome outcome) => outcome == BangOutcome.Keyed;

    private static BangReading Nothing(BangOutcome outcome) => new(outcome, null, default);

    private static BangReading Failed(BangRefusal why)
        => new(BangOutcome.Refused, why, new StreamWaitState(Failed: true));
}

/// <summary>
/// PP729: the handler's decisions, read out of streamconnection.c.
/// </summary>
public static class BangHandlerSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = StreamDispatchSource.RelativePath;

    /// <summary>And where the two buffer bounds are applied.</summary>
    public const string DecodeRelativePath = @"lib\src\pb_utils.h";

    /// <summary>streamconnection.c, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>pb_utils.h, or null outside a checkout.</summary>
    public static string? LocateDecode() => SanitizerSource.LocateRelative(DecodeRelativePath);

    /// <summary>The handler's body, or null where it is gone.</summary>
    public static string? HandlerBody(string source)
        => CFunction.Body(source, "static void stream_connection_takion_data_expect_bang(");

    /// <summary>
    /// The four buffer sizes and refusals in the order the body writes them, as text.
    ///
    /// Read as a SEQUENCE, because the order is what a log reader depends on: the first refusal
    /// that fires is the one reported, and two of the four can be true at once.
    /// </summary>
    public static IReadOnlyList<string> RefusalOrderIn(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        string[] tests =
        [
            "!msg.bang_payload.version_accepted",
            "!msg.bang_payload.encrypted_key_accepted",
            "!ecdh_pub_key_buf.size",
            "!ecdh_sig_buf.size",
        ];

        return
        [
            .. tests
                .Select(one => (Text: one, At: handlerBody.IndexOf(one, StringComparison.Ordinal)))
                .Where(one => one.At >= 0)
                .OrderBy(one => one.At)
                .Select(one => one.Text),
        ];
    }

    /// <summary>
    /// Whether a message that is not a bang still returns without touching either flag.
    ///
    /// The three early returns sit above the first `goto error`, so nothing between the top of the
    /// handler and the version test can set state_failed. A port that failed the state on an
    /// unexpected type would end a wait the C lets run.
    /// </summary>
    public static bool NotABangStillTouchesNeitherFlag(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int firstError = handlerBody.IndexOf("goto error;", StringComparison.Ordinal);
        if (firstError < 0)
            return false;

        string head = handlerBody[..firstError];

        return head.Contains("stream_connection_takion_data_handle_disconnect(", StringComparison.Ordinal)
            && head.Contains("streaminfo_early_buf = malloc(", StringComparison.Ordinal)
            && !head.Contains("state_failed", StringComparison.Ordinal)
            && !head.Contains("state_finished", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a second early streaminfo still falls through to the warning.
    ///
    /// The save's guard body returns; there is no else. So the first one is kept and a second is
    /// reported as an unexpected payload, which is the C saying nothing about having discarded it.
    /// </summary>
    public static bool ASecondEarlyStreaminfoStillFallsThrough(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int guard = handlerBody.IndexOf(
            "if(!stream_connection->streaminfo_early_buf)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int warned = handlerBody.IndexOf(
            "expected bang payload but received something else", guard, StringComparison.Ordinal);

        return warned > guard
            && !handlerBody[guard..warned].Contains("else", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the decode helper still fails the whole message on a field over its buffer.
    ///
    /// It sets the size to zero and returns FALSE, which nanopb reports as a decode failure - so an
    /// over-long ECDH key is the undecodable arm rather than a refusal, and the two arms leave
    /// different flags.
    /// </summary>
    public static bool AnOversizedFieldStillFailsTheDecode(string decodeSource)
    {
        ArgumentNullException.ThrowIfNull(decodeSource);

        string? body = CFunction.Body(decodeSource, "static inline bool chiaki_pb_decode_buf(");
        if (body is null)
            return false;

        int over = body.IndexOf("stream->bytes_left > buf->max_size", StringComparison.Ordinal);
        if (over < 0)
            return false;

        int refused = body.IndexOf("return false;", over, StringComparison.Ordinal);

        return refused > over && body.IndexOf("buf->size = 0;", over, StringComparison.Ordinal) < refused;
    }

    /// <summary>The two buffer sizes the handler declares, or null where either is gone.</summary>
    public static (int PubKey, int Sig)? BufferSizesIn(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int? pub = SizeOf(handlerBody, "char ecdh_pub_key[");
        int? sig = SizeOf(handlerBody, "char ecdh_sig[");

        return pub is null || sig is null ? null : (pub.Value, sig.Value);
    }

    private static int? SizeOf(string body, string declaration)
    {
        int at = body.IndexOf(declaration, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int from = at + declaration.Length;
        int end = body.IndexOf(']', from);

        return end > from && int.TryParse(body[from..end], out int size) ? size : null;
    }
}
