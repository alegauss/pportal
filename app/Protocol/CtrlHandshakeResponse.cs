using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which console answered, by the byte RP-Server-Type decrypts to.</summary>
public enum CtrlServerType
{
    /// <summary>A regular PS4. The one that cannot do 1080p.</summary>
    Ps4 = 0,

    /// <summary>A PS4 Pro. Can do 1080p, cannot do anything but H264.</summary>
    Ps4Pro = 1,

    /// <summary>A PS5.</summary>
    Ps5 = 2,
}

/// <summary>What the console's answer changed about the profile that was asked for.</summary>
/// <param name="Resolution">The resolution preset now being asked for, where one was forced down.</param>
/// <param name="Codec">The codec now being asked for, where one was forced down.</param>
/// <param name="Downgraded">Whether either was changed at all.</param>
public readonly record struct ProfileAfterServerType(
    ChiakiVideoResolution Resolution, ChiakiCodec Codec, bool Downgraded);

/// <summary>
/// PP360, under PP294: the answer to the ctrl request, and the counter it spends.
///
/// PP356 counted what the connect spends of the LOCAL crypt counter before sending. This is the
/// other side: the response can spend the REMOTE one, and whether it does depends on what the
/// console sent.
///
/// THE REMOTE COUNTER STARTS AT ONE OR AT ZERO. Where the answer carried a well-formed
/// RP-Server-Type, that header is decrypted at crypt_counter_remote++ and the first RECEIVED
/// control message decrypts at one. Where it was absent, or decoded to the wrong length, or failed
/// to decrypt, nothing is spent and the first message decrypts at zero. A port that picked either
/// unconditionally would be wrong against half the consoles it meets, and wrong silently.
///
/// THE REQUEST IS RETRIED EXACTLY ONCE, on timeout - a one-shot flag rather than a loop, like
/// PP334's version ladder is a count. On the TCP path the socket is torn down and reconnected
/// before the second attempt, because a timed-out connection is not one to reuse.
///
/// AND THE SERVER TYPE FORCES THE PROFILE DOWN. A regular PS4 asked for 1080p is dropped to 720p,
/// keeping whichever frame rate was chosen; a PS4 or a PS4 Pro asked for anything but H264 is
/// forced to H264. Both only where the header was valid - so PP358's case-sensitivity defect was
/// also a defect about asking a PS4 for a stream it cannot produce.
/// </summary>
public static class CtrlHandshakeResponse
{
    /// <summary>The decrypted RP-Server-Type is this many bytes, and is refused at any other length.</summary>
    public const int ServerTypeSize = 0x10;

    /// <summary>How many attempts the request ever makes. Not a loop bound - a count.</summary>
    public const int Attempts = 2;

    /// <summary>Whether a timeout on this attempt is retried.</summary>
    public static bool RetriesAfter(int attemptsSoFar) => attemptsSoFar < Attempts;

    /// <summary>
    /// Whether a timed-out TCP connection is reused for the retry.
    ///
    /// It is not: the socket is closed and reconnected. The rudp path has no socket of its own to
    /// rebuild, so it retries over what it has.
    /// </summary>
    public static bool ReconnectsBeforeRetrying(bool overRudp) => !overRudp;

    /// <summary>
    /// Whether the response counts as success, which is the HTTP code and nothing else.
    /// </summary>
    public static bool IsSuccess(int httpCode) => httpCode == 200;

    /// <summary>
    /// Whether a decoded RP-Server-Type is usable, which is a length question.
    ///
    /// The base64 is decoded into a fixed 16 bytes and the result is valid only where exactly that
    /// many came out. A shorter or longer value is not an error - it leaves server_type_valid false
    /// and the connect carries on without the downgrades.
    /// </summary>
    public static bool ServerTypeIsUsable(int decodedSize) => decodedSize == ServerTypeSize;

    /// <summary>
    /// What the remote crypt counter stands at when the first received message arrives.
    ///
    /// One where the server type was decrypted, zero where there was nothing to decrypt.
    /// </summary>
    public static uint RemoteCounterAfterResponse(bool serverTypeWasDecrypted)
        => serverTypeWasDecrypted ? 1u : 0u;

    /// <summary>
    /// What the console's type does to the profile that was asked for.
    /// </summary>
    /// <param name="serverType">The byte the header decrypted to.</param>
    /// <param name="asked">What the session asked for.</param>
    /// <param name="codec">The codec it asked for.</param>
    /// <param name="autoDowngrade">connect_info.video_profile_auto_downgrade.</param>
    public static ProfileAfterServerType Downgrade(
        CtrlServerType serverType,
        ChiakiVideoResolution asked,
        ChiakiCodec codec,
        bool autoDowngrade)
    {
        ChiakiVideoResolution resolution = asked;
        ChiakiCodec chosen = codec;
        var moved = false;

        // A regular PS4 cannot do 1080p - and only where the session allowed a downgrade at all.
        if (serverType == CtrlServerType.Ps4 && autoDowngrade && asked == ChiakiVideoResolution.P1080)
        {
            resolution = ChiakiVideoResolution.P720;
            moved = true;
        }

        // Neither PS4 can do anything but H264 - and this one is NOT gated on auto-downgrade, which
        // is the asymmetry: a session that refused a resolution downgrade still gets a codec one.
        if (serverType is CtrlServerType.Ps4 or CtrlServerType.Ps4Pro && codec != ChiakiCodec.H264)
        {
            chosen = ChiakiCodec.H264;
            moved = true;
        }

        return new ProfileAfterServerType(resolution, chosen, moved);
    }
}

/// <summary>
/// PP360: the response side held against ctrl.c. PP297's capture cannot judge it - the tap's first
/// ctrl entry is a LOGIN, and all of this happened before that.
/// </summary>
public static class CtrlHandshakeResponseSource
{
    /// <summary>Where it lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The connect's body, or null.</summary>
    public static string? ConnectBody(string filePath)
        => CFunction.BodyIn(filePath, "static ChiakiErrorCode ctrl_connect(");

    /// <summary>The response parser's body, or null.</summary>
    public static string? ParserBody(string filePath)
        => CFunction.BodyIn(filePath, "static void parse_ctrl_response(");

    /// <summary>Whether the retry is still one-shot rather than a loop.</summary>
    public static bool TheRetryIsStillOneShot(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return connectBody.Contains("bool ctrl_request_retry = false;", StringComparison.Ordinal)
            && connectBody.Contains("&& !ctrl_request_retry", StringComparison.Ordinal)
            && connectBody.Contains("ctrl_request_retry = true;", StringComparison.Ordinal);
    }

    /// <summary>Whether the TCP path still rebuilds its socket before the retry.</summary>
    public static bool TheSocketIsStillRebuiltBeforeTheRetry(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        int retry = connectBody.IndexOf("ctrl_request_retry = true;", StringComparison.Ordinal);
        if (retry < 0)
            return false;

        int disconnect = connectBody.IndexOf("ctrl_disconnect_tcp(ctrl);", retry, StringComparison.Ordinal);
        int reconnect = connectBody.IndexOf("ctrl_connect_tcp(ctrl);", retry, StringComparison.Ordinal);

        return disconnect > retry && reconnect > disconnect;
    }

    /// <summary>
    /// Whether the remote counter is still spent only where the server type is decrypted.
    ///
    /// The increment lives inside the branch. Moved out of it, every session's remote counter would
    /// start at one and half of them would decrypt to nothing.
    /// </summary>
    public static bool TheRemoteCounterIsStillSpentConditionally(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        int guard = connectBody.IndexOf("if(response.server_type_valid)", StringComparison.Ordinal);
        if (guard < 0)
            return false;

        int spend = connectBody.IndexOf("crypt_counter_remote++", guard, StringComparison.Ordinal);
        int nextGuard = connectBody.IndexOf(
            "if(response.server_type_valid)", guard + 1, StringComparison.Ordinal);

        // Inside the first branch, before the second one opens.
        return spend > guard && (nextGuard < 0 || spend < nextGuard);
    }

    /// <summary>Whether the codec downgrade is still ungated by auto-downgrade.</summary>
    public static bool TheCodecDowngradeIsStillUngated(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        int codec = connectBody.IndexOf(
            "session->connect_info.video_profile.codec != CHIAKI_CODEC_H264", StringComparison.Ordinal);
        if (codec < 0)
            return false;

        // The resolution branch names auto_downgrade; this one must not.
        int condition = connectBody.LastIndexOf("if(", codec, StringComparison.Ordinal);
        return condition >= 0
            && !connectBody[condition..codec].Contains("auto_downgrade", StringComparison.Ordinal);
    }

    /// <summary>Whether success is still the HTTP code alone.</summary>
    public static bool SuccessIsStillTheCodeAlone(string parserBody)
    {
        ArgumentNullException.ThrowIfNull(parserBody);

        return parserBody.Contains("if(http_response->code != 200)", StringComparison.Ordinal);
    }

    /// <summary>Whether the server type is still refused at any length but sixteen.</summary>
    public static bool TheServerTypeIsStillLengthChecked(string parserBody)
    {
        ArgumentNullException.ThrowIfNull(parserBody);

        return parserBody.Contains(
            "server_type_size == sizeof(response->rp_server_type)", StringComparison.Ordinal);
    }
}
