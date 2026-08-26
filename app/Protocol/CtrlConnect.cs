using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP356, under PP294: the ctrl request, and the crypt counter it spends before sending anything.
///
/// THE COUNTER IS THE THING TO GET RIGHT. Both counters start at zero, and the connect encrypts
/// between three and six values before a single control message goes out - each with
/// crypt_counter_local++. So the FIRST ctrl message is not encrypted at counter zero, and which
/// counter it is at depends on the target and on whether the session is over rudp. Get that number
/// wrong and every message decrypts to nothing, at the far end, with no local error.
///
/// The values are auth, did and ostype always; a start bitrate from PS4 target 10 upward; a
/// streaming type on a PS5. And on rudp with a PS5 the counter is bumped ONCE MORE for nothing at
/// all - a bare increment with no encryption, to stay in step with what the console expects.
///
/// THE STREAMING TYPE IS LITTLE-ENDIAN, alone. Every length and type in the message header is big
/// endian; this one field is assembled byte by byte from the low end. Both appear in the same
/// function, four lines apart.
///
/// THE PORT IS NOT IN THE URL. It comes from the holepunch session where there is one and from
/// SESSION_CTRL_PORT otherwise - the same 9295 the session request uses, which is why the two look
/// interchangeable and are not.
/// </summary>
public static class CtrlConnect
{
    /// <summary>The port the control channel connects to, absent a holepunched one.</summary>
    public const int CtrlPort = 9295;

    /// <summary>SESSION_OSTYPE, sent encrypted. Its NUL is part of what is encrypted.</summary>
    public const string OsType = "Win10.0.0";

    /// <summary>The path for a target, exactly as ctrl_connect's three branches choose it.</summary>
    public static string PathFor(ChiakiTarget target) => target switch
    {
        ChiakiTarget.Ps4_8 or ChiakiTarget.Ps4_9 => "/sce/rp/session/ctrl",
        _ when RpVersion.IsPs5(target) => "/sie/ps5/rp/sess/ctrl",
        _ => "/sie/ps4/rp/sess/ctrl",
    };

    /// <summary>Whether this target sends RP-StartBitrate.</summary>
    public static bool HasBitrate(ChiakiTarget target) => target >= ChiakiTarget.Ps4_10;

    /// <summary>Whether this target sends RP-StreamingType.</summary>
    public static bool HasStreamingType(ChiakiTarget target) => RpVersion.IsPs5(target);

    /// <summary>
    /// The streaming type for a codec: H265 is 2, HDR is 3, anything else - which is H264 - is 1.
    /// </summary>
    public static uint StreamingTypeFor(int codec) => codec switch
    {
        (int)ChiakiCodec.H265 => 2u,
        (int)ChiakiCodec.H265Hdr => 3u,
        _ => 1u,
    };

    /// <summary>
    /// The four bytes the streaming type is sent as - LITTLE-endian, unlike everything around it.
    /// </summary>
    public static byte[] StreamingTypeBytes(uint streamingType) =>
    [
        (byte)(streamingType & 0xff),
        (byte)((streamingType >> 8) & 0xff),
        (byte)((streamingType >> 16) & 0xff),
        (byte)((streamingType >> 24) & 0xff),
    ];

    /// <summary>
    /// How many times the connect spends the local crypt counter before the first ctrl message.
    ///
    /// Which is to say: the counter the first message will be encrypted at. Three values always,
    /// plus a bitrate, plus a streaming type, plus one thrown away on rudp with a PS5.
    /// </summary>
    public static uint CounterAfterConnect(ChiakiTarget target, bool overRudp)
    {
        uint spent = 3;

        if (HasBitrate(target))
            spent++;

        if (HasStreamingType(target))
            spent++;

        // Bumped for nothing, to stay in step with the console.
        if (overRudp && RpVersion.IsPs5(target))
            spent++;

        return spent;
    }

    /// <summary>
    /// The headers the request carries, in order, for a target - the fixed ones and the two
    /// conditional ones.
    ///
    /// Named rather than formatted, because the values are base64 of freshly encrypted bytes and a
    /// test cannot predict them. What it can hold is WHICH headers go and in what order: the format
    /// string appends the two conditional ones at the end, so a port emitting them in declaration
    /// order would send a request the console reads differently.
    /// </summary>
    public static IReadOnlyList<string> HeadersFor(ChiakiTarget target)
    {
        var headers = new List<string>
        {
            "Host", "User-Agent", "Connection", "Content-Length",
            "RP-Auth", "RP-Version", "RP-Did",
            "RP-ControllerType", "RP-ClientType", "RP-OSType", "RP-ConPath",
        };

        if (HasBitrate(target))
            headers.Add("RP-StartBitrate");

        if (HasStreamingType(target))
            headers.Add("RP-StreamingType");

        return headers;
    }

    /// <summary>
    /// The two values the request states outright rather than deriving.
    ///
    /// RP-ControllerType 3 and RP-ClientType 11 are literals in the format string with nothing in
    /// the tree explaining either, which is worth carrying across as literals rather than as
    /// something a reader might think is computed.
    /// </summary>
    public static (string ControllerType, string ClientType) FixedTypes => ("3", "11");
}

/// <summary>
/// PP356: the connect held against ctrl.c, since PP297's capture starts after it - the tap's first
/// ctrl entry is a LOGIN, and everything here happened before that.
/// </summary>
public static class CtrlConnectSource
{
    /// <summary>Where it lives.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The connect's body, or null.</summary>
    public static string? ConnectBody(string filePath)
        => CFunction.BodyIn(filePath, "static ChiakiErrorCode ctrl_connect(");

    /// <summary>
    /// How many times the body spends the local counter, counted rather than assumed.
    ///
    /// Every occurrence, including the bare increment on the rudp PS5 path - which is the one a
    /// reader skips, because it is not next to an encrypt.
    /// </summary>
    public static int CounterSpendsIn(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        var spends = 0;
        for (int at = connectBody.IndexOf("crypt_counter_local++", StringComparison.Ordinal);
             at >= 0;
             at = connectBody.IndexOf("crypt_counter_local++", at + 1, StringComparison.Ordinal))
        {
            spends++;
        }

        return spends;
    }

    /// <summary>Whether both counters still start at zero.</summary>
    public static bool BothCountersStillStartAtZero(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return connectBody.Contains("crypt_counter_local = 0;", StringComparison.Ordinal)
            && connectBody.Contains("crypt_counter_remote = 0;", StringComparison.Ordinal);
    }

    /// <summary>Whether the streaming type is still assembled from the low byte up.</summary>
    public static bool TheStreamingTypeIsStillLittleEndian(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        int at = connectBody.IndexOf("streaming_type_buf[4] = {", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = connectBody.IndexOf('}', at);
        if (end < 0)
            return false;

        string spelled = connectBody[at..end];

        // The low byte first, then 8, 0x10, 0x18 - which is the order that makes it little-endian.
        int low = spelled.IndexOf("streaming_type & 0xff", StringComparison.Ordinal);
        int high = spelled.IndexOf(">> 0x18", StringComparison.Ordinal);

        return low >= 0 && high > low;
    }

    /// <summary>Whether the two conditional headers are still appended after the fixed ones.</summary>
    public static bool TheConditionalHeadersAreStillLast(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        int conPath = connectBody.IndexOf("\"RP-ConPath: 1\\r\\n\"", StringComparison.Ordinal);
        int slots = connectBody.IndexOf("\"%s%s%s\"", StringComparison.Ordinal);

        return conPath >= 0 && slots > conPath;
    }

    /// <summary>Whether the three paths are still the ones the target picks between.</summary>
    public static bool ThePathsAreStill(string connectBody)
    {
        ArgumentNullException.ThrowIfNull(connectBody);

        return connectBody.Contains("\"/sce/rp/session/ctrl\"", StringComparison.Ordinal)
            && connectBody.Contains("\"/sie/ps5/rp/sess/ctrl\"", StringComparison.Ordinal)
            && connectBody.Contains("\"/sie/ps4/rp/sess/ctrl\"", StringComparison.Ordinal);
    }
}
