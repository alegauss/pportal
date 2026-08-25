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
    /// One function's body, from its signature to the brace that closes it.
    ///
    /// THE FIRST MATCH IS NOT THE FUNCTION. Both sites this reads are declared at the top of ctrl.c
    /// before they are defined - the file's own style - so a search that took the first occurrence
    /// lands on a prototype ending in a semicolon, walks forward to the next `{` in the file, and
    /// bounds a body belonging to something else entirely. That checked out green while comparing
    /// two positions in a function neither call is in.
    ///
    /// So a match is only the definition when what follows its parameter list is a brace. Brace
    /// counted from there rather than matched to a blank line, because ctrl.c's functions contain
    /// blank lines and its style puts the opening brace on its own.
    /// </summary>
    private static string? Function(string source, string signature)
    {
        for (int start = source.IndexOf(signature, StringComparison.Ordinal);
             start >= 0;
             start = source.IndexOf(signature, start + signature.Length, StringComparison.Ordinal))
        {
            int close = source.IndexOf(')', start);
            if (close < 0)
                return null;

            int open = close + 1;
            while (open < source.Length && char.IsWhiteSpace(source[open]))
                open++;

            // A prototype. Keep looking; the definition is further down.
            if (open >= source.Length || source[open] != '{')
                continue;

            var depth = 0;
            for (int at = open; at < source.Length; at++)
            {
                if (source[at] == '{')
                    depth++;
                else if (source[at] == '}' && --depth == 0)
                    return source[start..(at + 1)];
            }

            return null;
        }

        return null;
    }
}
