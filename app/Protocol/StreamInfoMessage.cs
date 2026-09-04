using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One video profile the console announced.</summary>
/// <param name="Width">The picture's width in pixels.</param>
/// <param name="Height">And its height.</param>
/// <param name="Header">
/// The codec header a decoder needs before any picture, PADDED: sixty-four zero bytes follow it,
/// because the decoder reads past the end of what it is given.
/// </param>
/// <param name="HeaderLength">
/// How much of <paramref name="Header"/> the console actually sent, which is what the C keeps
/// alongside the buffer as header_sz. The padding is not part of the header.
/// </param>
public readonly record struct VideoProfile(uint Width, uint Height, byte[] Header, int HeaderLength);

/// <summary>What the streaminfo handler decided about one message.</summary>
public enum StreamInfoVerdict
{
    /// <summary>Everything held: the profiles are the receiver's to take.</summary>
    Accepted,

    /// <summary>The protobuf did not decode. The C logs and returns.</summary>
    Undecodable,

    /// <summary>It decoded and is not a streaminfo, which the C hexdumps and drops.</summary>
    NotStreamInfo,

    /// <summary>It is a DISCONNECT, which the C routes to the disconnect handler instead.</summary>
    Disconnect,

    /// <summary>The audio header was not the size the audio receiver loads.</summary>
    AudioHeaderWrongSize,
}

/// <summary>What one STREAMINFO turned out to hold.</summary>
/// <param name="Verdict">Which of the five.</param>
/// <param name="Profiles">
/// What the resolutions decoded to, and they are present even where the verdict refuses the
/// message: the callback runs during the decode, so by the time the audio header is looked at the
/// resolutions already exist. That is PP372's whole point about who owns them.
/// </param>
/// <param name="AudioHeader">The fourteen bytes, or what arrived where the size was wrong.</param>
/// <param name="Announced">How many resolutions the console announced, before the cap.</param>
public readonly record struct StreamInfoReading(
    StreamInfoVerdict Verdict,
    IReadOnlyList<VideoProfile> Profiles,
    byte[]? AudioHeader,
    int Announced);

/// <summary>
/// PP686, under PP295: the message that tells the port what the stream is.
///
/// STREAMINFO is the console's answer to the handshake and the only one that says what the stream
/// will be - a resolution per profile, each carrying the codec header a decoder needs before any
/// picture, and one audio header beside them. PP424's participant recognised it and answered with
/// three messages; nothing read what it carried, so ManagedVideoReceiver.StreamInfo had never been
/// handed a profile a console chose.
///
/// THE DECODE IS THE GENERATOR'S, THE RULES ARE THE PORT'S. The C hands the bytes to nanopb with a
/// callback per repeated field; this hands them to the types protoc generated from the same .proto,
/// which is the same division of labour one language over. What is ported is what surrounds the
/// decode, and three of those four rules are PP372's repairs:
///
///   the header is PADDED by sixty-four zero bytes, because the decoder reads past its end;
///
///   the profile count is capped at eight, and in the C the check sits ABOVE the realloc - below it,
///   a console announcing nine had every one padded and the surplus dropped with nothing owning it.
///   Managed code has no realloc to move, so what carries the rule here is which profiles are KEPT,
///   and <see cref="StreamInfoReading.Announced"/> keeps the console's own number so a cap that
///   started silently discarding is still visible;
///
///   an audio header that is not <see cref="AudioHeaderSize"/> bytes refuses the whole message, with
///   the resolutions already decoded - which is the path PP372 called the reason the leak was worth
///   a task rather than a note. PP687 split that in two: SHORTER is a wrong size, LONGER is no
///   message at all, because the C's bounded read refuses rather than truncating.
///
/// The fourth is ownership, and it needs no code here: a reading either reaches
/// <see cref="ManagedVideoReceiver.StreamInfo"/> or does not, and the garbage collector is what the
/// C spends decode_resolutions_context_free on.
///
/// THE ORDER OF THE REFUSALS IS THE C'S. A DISCONNECT arriving where a streaminfo was expected is
/// routed rather than dropped, and it is tested before the "something else" branch - so a console
/// hanging up during setup is a disconnect and not an unknown message.
/// </summary>
public static class StreamInfoMessage
{
    /// <summary>CHIAKI_AUDIO_HEADER_SIZE, which the audio receiver loads and nothing else fits.</summary>
    public const int AudioHeaderSize = 0xe;

    /// <summary>
    /// PP687: the bound the DECODE puts on the audio header, which is the same number read the other
    /// way round.
    ///
    /// The C hands nanopb a fourteen-byte buffer and chiaki_pb_decode_buf refuses - sets the size to
    /// zero and returns false - for anything longer, which fails the whole pb_decode. So fifteen
    /// bytes is not a wrong size, it is no message: the handler takes the failed-decode branch and
    /// never reaches the check that would have called it wrong. Thirteen decodes and is then refused
    /// for its size, which is the case they are easy to confuse.
    /// </summary>
    public const int AudioHeaderDecodeBound = AudioHeaderSize;

    /// <summary>CHIAKI_VIDEO_BUFFER_PADDING_SIZE - what follows every header, zeroed.</summary>
    public const int PaddingBytes = 64;

    /// <summary>CHIAKI_VIDEO_PROFILES_MAX, through PP372's model of it.</summary>
    public const int ProfilesMax = VideoProfileOwnership.ProfilesMax;

    /// <summary>A header with the padding a decoder reads into.</summary>
    public static byte[] Padded(ReadOnlySpan<byte> header)
    {
        byte[] padded = new byte[header.Length + PaddingBytes];
        header.CopyTo(padded);

        // The rest is already zero, which is what the C's memset makes it.
        return padded;
    }

    /// <summary>
    /// Reads one message where a streaminfo is expected, as the C's handler reads it.
    /// </summary>
    /// <param name="message">The whole TakionMessage, as the data handler receives it.</param>
    public static StreamInfoReading Read(ReadOnlySpan<byte> message)
    {
        Tkproto.TakionMessage decoded;

        try
        {
            decoded = Tkproto.TakionMessage.Parser.ParseFrom(message.ToArray());
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            return new StreamInfoReading(StreamInfoVerdict.Undecodable, [], null, 0);
        }

        // The resolutions are read whatever the message turns out to be, because the C's callback
        // runs inside the decode. A refusal below still reports them.
        int announced = decoded.StreamInfoPayload?.Resolution.Count ?? 0;
        IReadOnlyList<VideoProfile> profiles = ProfilesIn(decoded);

        if (decoded.Type != Tkproto.TakionMessage.Types.PayloadType.Streaminfo
            || decoded.StreamInfoPayload is null)
        {
            StreamInfoVerdict verdict =
                decoded.Type == Tkproto.TakionMessage.Types.PayloadType.Disconnect
                    ? StreamInfoVerdict.Disconnect
                    : StreamInfoVerdict.NotStreamInfo;

            return new StreamInfoReading(verdict, profiles, null, announced);
        }

        byte[] audio = decoded.StreamInfoPayload.AudioHeader.ToByteArray();

        // PP687: above the bound there is no message. protoc's parser has no per-field buffer to
        // refuse with, so the C's refusal is applied here rather than being inherited - and it is
        // applied BEFORE the size check, because in the C the decode is what runs first.
        if (audio.Length > AudioHeaderDecodeBound)
            return new StreamInfoReading(StreamInfoVerdict.Undecodable, profiles, audio, announced);

        if (audio.Length != AudioHeaderSize)
            return new StreamInfoReading(StreamInfoVerdict.AudioHeaderWrongSize, profiles, audio, announced);

        return new StreamInfoReading(StreamInfoVerdict.Accepted, profiles, audio, announced);
    }

    /// <summary>
    /// The headers a reading hands the receiver, padded and in the order the console announced them.
    ///
    /// The receiver takes them in one call or not at all, so this is the whole of the handover: an
    /// array, and whether the reading was one to hand over at all is the caller's to test.
    /// </summary>
    public static byte[][] HeadersFor(StreamInfoReading reading)
        => [.. reading.Profiles.Select(profile => profile.Header)];

    private static IReadOnlyList<VideoProfile> ProfilesIn(Tkproto.TakionMessage decoded)
    {
        if (decoded.StreamInfoPayload is null)
            return [];

        var profiles = new List<VideoProfile>();

        foreach (Tkproto.ResolutionPayload resolution in decoded.StreamInfoPayload.Resolution)
        {
            // A resolution whose header did not decode is skipped and does not count against the
            // room, which is the C's `if(!header_buf.buf) return true` - it consumed the field and
            // kept going rather than refusing the message.
            if (resolution.VideoHeader.IsEmpty)
                continue;

            // PP372: the cap BEFORE the padding. In the C that ordering is what stops a header being
            // allocated for a profile there is no room for; here it is what stops one being kept.
            if (profiles.Count >= ProfilesMax)
                continue;

            byte[] header = resolution.VideoHeader.ToByteArray();
            profiles.Add(new VideoProfile(
                resolution.Width, resolution.Height, Padded(header), header.Length));
        }

        return profiles;
    }
}

/// <summary>
/// PP686: the four rules held against the C that has them.
///
/// PP372 already reads the ordering of the cap and the padding, and the exits that owe a free;
/// what this adds is the two constants and the refusal order, which are what a managed parse gets
/// wrong silently - a message read with the wrong audio-header size accepts what the console's own
/// audio receiver would refuse.
/// </summary>
public static class StreamInfoMessageSource
{
    /// <summary>Where the handler lives.</summary>
    public const string RelativePath = VideoProfileOwnershipSource.StreamRelativePath;

    /// <summary>Where the audio header's size is defined.</summary>
    public const string AudioHeaderRelativePath = @"lib\include\chiaki\audio.h";

    /// <summary>And the padding.</summary>
    public const string VideoHeaderRelativePath = @"lib\include\chiaki\video.h";

    /// <summary>One of them, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>CHIAKI_AUDIO_HEADER_SIZE as audio.h defines it.</summary>
    public static long? AudioHeaderSizeIn(string audioHeader)
        => CDefine.Value(audioHeader, "CHIAKI_AUDIO_HEADER_SIZE");

    /// <summary>CHIAKI_VIDEO_BUFFER_PADDING_SIZE as video.h defines it.</summary>
    public static long? PaddingIn(string videoHeader)
        => CDefine.Value(videoHeader, "CHIAKI_VIDEO_BUFFER_PADDING_SIZE");

    /// <summary>The streaminfo handler's body, or null where it is gone.</summary>
    public static string? HandlerBody(string streamCore)
        => CFunction.Body(streamCore, "static void stream_connection_takion_data_expect_streaminfo(");

    /// <summary>
    /// Whether a DISCONNECT arriving here is still routed rather than dropped, and still tested
    /// before the "something else" branch.
    ///
    /// Both halves: the branch existing says a hang-up during setup is understood, and its position
    /// says it is understood FIRST - after the generic refusal it would never be reached.
    /// </summary>
    public static bool ADisconnectIsStillRoutedFirst(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int disconnect = handlerBody.IndexOf(
            "msg.type == tkproto_TakionMessage_PayloadType_DISCONNECT", StringComparison.Ordinal);
        int routed = handlerBody.IndexOf(
            "stream_connection_takion_data_handle_disconnect(", StringComparison.Ordinal);
        int somethingElse = handlerBody.IndexOf(
            "expected streaminfo payload but received something else", StringComparison.Ordinal);

        return disconnect >= 0 && routed > disconnect && somethingElse > routed;
    }

    /// <summary>
    /// Whether the audio header's size is still checked before the receivers are handed anything.
    ///
    /// The order is what makes a wrong size a refusal rather than a bad load: past it, the audio
    /// header is loaded and the profiles are handed over.
    /// </summary>
    public static bool TheAudioSizeIsCheckedBeforeEitherHandover(string handlerBody)
    {
        ArgumentNullException.ThrowIfNull(handlerBody);

        int size = handlerBody.IndexOf(
            "audio_header_buf.size != CHIAKI_AUDIO_HEADER_SIZE", StringComparison.Ordinal);
        int audio = handlerBody.IndexOf("chiaki_audio_receiver_stream_info(", StringComparison.Ordinal);
        int video = handlerBody.IndexOf("chiaki_video_receiver_stream_info(", StringComparison.Ordinal);

        return size >= 0 && audio > size && video > audio;
    }
}
