using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP323: where the tap is emitted from, asserted against the C rather than trusted.
///
/// The tap's whole value is that it carries PLAINTEXT. Every one of the four sites sits in a window
/// one statement wide, and each window has a different way of closing:
///
///   ctrl_message_send emits BEFORE chiaki_rpcrypt_encrypt. A line moved below that call still
///   compiles, still runs, still records - and records ciphertext, which replays against nothing.
///
///   ctrl_message_received emits AFTER chiaki_rpcrypt_decrypt and BEFORE the type switch. Above the
///   decrypt is ciphertext; below the switch the message is a handler's arguments and no longer one
///   thing.
///
///   The session request emits before chiaki_send_fully, and its answer before
///   chiaki_http_response_parse - after which the header is a struct of pointers into the same
///   buffer, so a recording of it would be a recording of the parser.
///
/// None of those four failures produces an error, a warning or a short recording. They produce a
/// recording of the wrong bytes, discovered by a replay that disagrees for a reason nobody can find.
/// So the ORDER is what is checked here, not the presence.
/// </summary>
public static class MessageTapSource
{
    /// <summary>The control channel's two sites.</summary>
    public const string CtrlSource = @"lib\src\ctrl.c";

    /// <summary>The session request and its answer.</summary>
    public const string SessionSource = @"lib\src\session.c";

    /// <summary>The tap itself, which is where the sites' contract is written down.</summary>
    public const string TapHeader = @"lib\include\chiaki\messagetap.h";

    /// <summary>
    /// PP394: senkusha's two, which PP393 said had to be found rather than chosen.
    ///
    /// PP323's four sites cover ctrl.c and session.c, and those are two of the four modules PP23
    /// names as untested - the two PP391 and PP392 replayed. streamconnection.c and senkusha.c had
    /// no channel at all, so no recording could hold them.
    /// </summary>
    public const string SenkushaSource = @"lib\src\senkusha.c";

    /// <summary>
    /// Whether senkusha's protobuf sends still go through one place.
    ///
    /// THE CHOKEPOINT IS MADE, NOT FOUND, and that is the difference from the other four. ctrl.c had
    /// its window in ctrl_message_send; senkusha spread the same window over six call sites, which
    /// is why PP393 said the site was not obvious. senkusha_send_data is that window introduced, and
    /// this asserts every send is behind it - a seventh added straight onto takion would be a
    /// message no recording holds.
    /// </summary>
    public static bool TheSenkushaSendsStillGoThroughOnePlace(string senkusha)
    {
        ArgumentNullException.ThrowIfNull(senkusha);

        // The helper taps and then forwards, in that order: after the transport call the buffer is
        // the transport's business and the send may already have failed.
        string? body = CFunction.Body(senkusha, "ChiakiErrorCode senkusha_send_data(");
        if (body is null)
            return false;

        int tapped = body.IndexOf("CHIAKI_MESSAGE_TAP_CHANNEL_SENKUSHA", StringComparison.Ordinal);
        int sent = body.IndexOf("chiaki_takion_send_message_data(", tapped < 0 ? 0 : tapped, StringComparison.Ordinal);
        if (tapped < 0 || sent < tapped)
            return false;

        // And nothing else in the file reaches the transport directly.
        return CCall.Count(senkusha, "chiaki_takion_send_message_data(&senkusha->takion, 1, data_type, buf, buf_size, seq_num_out)") == 1
            && OtherTransportSendsIn(senkusha) == 0;
    }

    /// <summary>
    /// How many protobuf sends bypass the chokepoint, which is the number that must stay zero.
    /// </summary>
    public static int OtherTransportSendsIn(string senkusha)
    {
        ArgumentNullException.ThrowIfNull(senkusha);

        // Every call to the transport, less the one inside the helper.
        return CCall.Count(senkusha, "chiaki_takion_send_message_data(") - 1;
    }

    /// <summary>
    /// Whether the received protobuf is still tapped before it becomes a handler's arguments.
    ///
    /// PP323's rule for ctrl.c:937, read across: above the decode it is the bytes that arrived, and
    /// below it the message is a struct nobody can replay.
    /// </summary>
    public static bool TheSenkushaReceiveIsStillTappedBeforeTheDecode(string senkusha)
    {
        ArgumentNullException.ThrowIfNull(senkusha);

        string? body = CFunction.Body(senkusha, "void senkusha_takion_data(");
        if (body is null)
            return false;

        int tapped = body.IndexOf("CHIAKI_MESSAGE_TAP_RECEIVED", StringComparison.Ordinal);
        int decoded = body.IndexOf("pb_decode(", tapped < 0 ? 0 : tapped, StringComparison.Ordinal);

        return tapped >= 0 && decoded > tapped;
    }

    /// <summary>PP395: the fourth and last of PP23's modules to get a channel.</summary>
    public const string StreamSource = @"lib\src\streamconnection.c";

    /// <summary>
    /// Whether the stream connection's unfragmented sends still go through one place.
    ///
    /// Eight of them did not, which is the same shape senkusha had. The BIG is deliberately not
    /// among them - it taps itself before the fragmentation, and
    /// <see cref="TheBigIsStillTappedWholeBeforeItIsFragmented"/> is that half.
    /// </summary>
    public static bool TheStreamSendsStillGoThroughOnePlace(string stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string? body = CFunction.Body(stream, "ChiakiErrorCode stream_connection_send_data(");
        if (body is null)
            return false;

        int tapped = body.IndexOf("CHIAKI_MESSAGE_TAP_CHANNEL_STREAM", StringComparison.Ordinal);
        int sent = body.IndexOf(
            "chiaki_takion_send_message_data(", tapped < 0 ? 0 : tapped, StringComparison.Ordinal);

        return tapped >= 0 && sent > tapped && OtherStreamSendsIn(stream) == 0;
    }

    /// <summary>
    /// How many sends bypass the chokepoint, not counting the BIG's fragments.
    ///
    /// The four in the fragment loop are the BIG's and are excluded BY SHAPE rather than by count:
    /// every one of them sends `buf + buf_pos`, which is a slice, and no other send in the file
    /// does. A ninth ordinary send written straight onto takion would pass `buf` and be caught.
    /// </summary>
    public static int OtherStreamSendsIn(string stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        int direct = CCall.Count(stream, "chiaki_takion_send_message_data(")
            + CCall.Count(stream, "chiaki_takion_send_message_data_cont(");

        // One inside the chokepoint, and four fragment sends that carry a slice.
        int fragments = CCall.Count(stream, "buf + buf_pos, buf_size, NULL")
            + CCall.Count(stream, "buf + buf_pos, total_size, NULL");

        return direct - 1 - fragments;
    }

    /// <summary>
    /// Whether the BIG is still tapped whole, before the loop that cuts it up.
    ///
    /// PP375 measured that the number of fragments follows the negotiated MTU. A recording of
    /// fragments would therefore replay only against a run that measured the same link, which is
    /// the opposite of an oracle - so the tap sits above the loop and carries the whole message.
    /// </summary>
    public static bool TheBigIsStillTappedWholeBeforeItIsFragmented(string stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string? body = CFunction.Body(stream, "ChiakiErrorCode stream_connection_send_big(");
        if (body is null)
            return false;

        int tapped = body.IndexOf("CHIAKI_MESSAGE_TAP_CHANNEL_STREAM", StringComparison.Ordinal);
        int loop = body.IndexOf("while(first ?", tapped < 0 ? 0 : tapped, StringComparison.Ordinal);

        // Tapped, then fragmented - and with the whole encoded length rather than a fragment's.
        return tapped >= 0
            && loop > tapped
            && body[tapped..loop].Contains("stream.bytes_written", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the received protobuf is still tapped ABOVE the state lock.
    ///
    /// PP366 established that the lock spans the whole switch so the run thread can read
    /// state_finished the moment its wait returns. A tap handler called under it would hold that
    /// window open for as long as a recorder takes to write a line, which is a diagnostic changing
    /// the timing of the thing it observes.
    /// </summary>
    public static bool TheStreamReceiveIsStillTappedAboveTheLock(string stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string? body = CFunction.Body(stream, "void stream_connection_takion_data_protobuf(");
        if (body is null)
            return false;

        int tapped = body.IndexOf("CHIAKI_MESSAGE_TAP_RECEIVED", StringComparison.Ordinal);
        int locked = body.IndexOf(
            "chiaki_mutex_lock(&stream_connection->state_mutex)", tapped < 0 ? 0 : tapped,
            StringComparison.Ordinal);

        return tapped >= 0 && locked > tapped;
    }

    /// <summary>One of the three, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// Whether the send site still emits before the encrypt.
    ///
    /// Bounded to the function rather than searched file-wide: ctrl.c calls chiaki_rpcrypt_encrypt
    /// in more than one place, and a check that took the first one anywhere would be comparing this
    /// emit against a call in a different function.
    /// </summary>
    public static bool TheSendSiteStillEmitsBeforeTheEncrypt(string ctrl)
    {
        ArgumentNullException.ThrowIfNull(ctrl);

        string? body = Function(ctrl, "static ChiakiErrorCode ctrl_message_send(");
        if (body is null)
            return false;

        int emit = body.IndexOf("CHIAKI_MESSAGE_TAP_SENT", StringComparison.Ordinal);
        int encrypt = body.IndexOf("chiaki_rpcrypt_encrypt", StringComparison.Ordinal);

        return emit >= 0 && encrypt >= 0 && emit < encrypt;
    }

    /// <summary>Whether the receive site still emits after the decrypt and before the switch.</summary>
    public static bool TheReceiveSiteStillEmitsBetweenTheDecryptAndTheSwitch(string ctrl)
    {
        ArgumentNullException.ThrowIfNull(ctrl);

        string? body = Function(ctrl, "static void ctrl_message_received(");
        if (body is null)
            return false;

        int decrypt = body.IndexOf("chiaki_rpcrypt_decrypt", StringComparison.Ordinal);
        int emit = body.IndexOf("CHIAKI_MESSAGE_TAP_RECEIVED", StringComparison.Ordinal);
        int dispatch = body.IndexOf("switch(msg_type)", StringComparison.Ordinal);

        return decrypt >= 0 && emit > decrypt && dispatch > emit;
    }

    /// <summary>Whether the session request is still tapped before it is sent.</summary>
    public static bool TheSessionRequestIsStillTappedBeforeItIsSent(string session)
    {
        ArgumentNullException.ThrowIfNull(session);

        int emit = session.IndexOf(
            "CHIAKI_MESSAGE_TAP_SENT, CHIAKI_MESSAGE_TAP_CHANNEL_SESSION", StringComparison.Ordinal);
        if (emit < 0)
            return false;

        int send = session.IndexOf("chiaki_send_fully", emit, StringComparison.Ordinal);
        return send > emit;
    }

    /// <summary>Whether the answer is still tapped before the parse turns it into pointers.</summary>
    public static bool TheSessionResponseIsStillTappedBeforeTheParse(string session)
    {
        ArgumentNullException.ThrowIfNull(session);

        int emit = session.IndexOf(
            "CHIAKI_MESSAGE_TAP_RECEIVED, CHIAKI_MESSAGE_TAP_CHANNEL_SESSION", StringComparison.Ordinal);
        if (emit < 0)
            return false;

        int parse = session.IndexOf("chiaki_http_response_parse", emit, StringComparison.Ordinal);
        return parse > emit;
    }

    /// <summary>
    /// Whether the tap is still off by default, which is what makes the four sites free.
    ///
    /// A tap whose pointer started non-null would call into whatever the initialiser named on every
    /// control message of every session, recording included or not.
    /// </summary>
    public static bool TheTapIsStillOffUntilItIsSet(string tapSource)
    {
        ArgumentNullException.ThrowIfNull(tapSource);
        return tapSource.Contains("static ChiakiMessageTapCb chiaki_message_tap_cb = NULL;", StringComparison.Ordinal);
    }

    /// <summary>
    /// One function's body. PP343 moved the reader itself to <see cref="CFunction"/>, which is where
    /// the note about prototypes now lives - this file had the only correct copy of it and two
    /// others were written without finding it.
    /// </summary>
    private static string? Function(string source, string signature)
        => CFunction.Body(source, signature);
}
