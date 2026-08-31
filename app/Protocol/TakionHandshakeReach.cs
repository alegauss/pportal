using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP602, under PP27: what has to sit on the other end of PP601's socket, and why it cannot be a
/// recording.
///
/// PP601 found the way into takion's receive loop that needs no patch: chiaki_takion_connect takes
/// the caller's own socket, so a local pair supplies the socket and thread §PP27 says a capture
/// lacks. The obvious next thought is to put PP297's recorded console on the far end and let the
/// loop run against real traffic. It does not work, and the reason is one line of C.
///
/// THE TAG IS FRESH EVERY CONNECT. takion.c sets <c>tag_local = chiaki_random_32()</c> and takes
/// <c>seq_num_local</c> from it, inside connect, before the thread starts. ChiakiTakionConnectInfo
/// carries the log, the address, the callback, the crypt and dualsense flags, the protocol version
/// and close_socket - and no tag and no sequence number. So a caller cannot ask for the recorded
/// run's values, and the handshake a recording answers is not the handshake a fresh takion sends.
///
/// SO THE FAR END IS A RESPONDER. The exchange opens INIT -&gt; INIT_ACK, retried up to
/// MAX_CONNECT_RESEND_TRIES, and the ack has to answer the tag it was just sent rather than one
/// captured months ago. That is a peer that parses and replies, not a tape.
///
/// WHICH IS SMALLER THAN IT SOUNDS, and is where the port is already strong: the managed side
/// models takion's messages. A responder on the far end of a local socket is managed code driving
/// the real C loop - the direction this port is going anyway, and the capture supplies the DATA
/// packets once the handshake is past.
/// </summary>
public static class TakionHandshakeReach
{
    /// <summary>Where the tag is drawn.</summary>
    public const string RandomCall = "chiaki_random_32()";

    /// <summary>The field it is drawn into.</summary>
    public const string TagField = "tag_local";

    /// <summary>The sequence number taken from it.</summary>
    public const string SeqField = "seq_num_local";

    /// <summary>The first message the client sends, which the far end has to answer.</summary>
    public const string FirstMessage = "takion_send_message_init";

    /// <summary>
    /// Whether the tag is still generated inside connect rather than supplied by the caller.
    ///
    /// Both halves of the assignment, because a tag read from the connect info would be the one
    /// change that makes a recording usable - and it would arrive as an ordinary-looking edit.
    /// </summary>
    public static bool TheTagIsDrawnFresh(string takionSource)
    {
        ArgumentNullException.ThrowIfNull(takionSource);

        foreach (string line in takionSource.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (trimmed.Contains(TagField + " = " + RandomCall, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The fields ChiakiTakionConnectInfo carries, as takion.h declares them.
    ///
    /// Listed so the absence is a statement rather than a failed search: what makes a recording
    /// unusable is that neither a tag nor a sequence number is among these.
    /// </summary>
    public static IReadOnlyList<string> ConnectInfoFields { get; } =
    [
        "log", "sa", "sa_len", "ip_dontfrag", "cb", "cb_user",
        "disable_audio_video", "enable_crypt", "enable_dualsense",
        "protocol_version", "close_socket",
    ];

    /// <summary>Whether the caller can hand takion a tag or a sequence number. It cannot.</summary>
    public static bool TheCallerCanSupplyTheTag(string takionHeader)
    {
        ArgumentNullException.ThrowIfNull(takionHeader);

        int at = takionHeader.IndexOf(
            "typedef struct chiaki_takion_connect_info", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = takionHeader.IndexOf("ChiakiTakionConnectInfo;", at, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string body = takionHeader[at..end];

        return body.Contains(TagField, StringComparison.Ordinal)
            || body.Contains(SeqField, StringComparison.Ordinal);
    }
}
