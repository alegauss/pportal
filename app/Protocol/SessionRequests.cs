using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: the four JSON bodies a session is set up with, all built from string templates for the
/// reason PP196 found - "the broken JSON used by the official app, which we're trying to emulate".
///
/// THREE FIELDS, ONE WORD. The create body sends <c>accountId</c>, <c>deviceUniqueId</c> and
/// <c>platform</c> all as the literal string "me". They are three different things and the request
/// says the same placeholder for each, because the server fills them from the token. A port that
/// helpfully sent the real account id, the real device id and the real platform would be sending
/// something the official app never sends.
///
/// THE ACCOUNT ID IS AN INTEGER IN ONE PAYLOAD AND A STRING IN TWO OTHERS. The start payload writes
/// it bare; the message envelope and the local peer address both quote it. Same value, same
/// session, three fields, two types - and PP183 established that json-c would have coerced either
/// way, so nothing here forced the difference.
///
/// THIS CLIENT HAS THREE NAMES FOR ITSELF. It is "Windows" in the start payload's clientType,
/// "REMOTE_PLAY" in the local peer address's platform (PP196), and "me" in the create body's. None
/// of the three is derived from either of the others.
///
/// AND customData1 IS LENGTH-GATED BEFORE IT IS DECODED. Exactly thirty-two characters or the
/// session start fails - the check that runs before PP192's two-round decode, and which PP192 could
/// not see from where it sat.
///
/// ONE field in all four templates carries a space after its colon: the wake-up body's roomId. The
/// same field in the start payload does not. It changes nothing on the wire; it is a tell that
/// these were transcribed from captured traffic rather than written.
/// </summary>
public static class SessionRequests
{
    /// <summary>The word the create body sends for all three of its identity fields.</summary>
    public const string CreatePlaceholder = "me";

    /// <summary>What the start payload calls this client.</summary>
    public const string ClientType = "Windows";

    /// <summary>The protocol version it claims.</summary>
    public const string ProtocolVersion = "10.0";

    /// <summary>The room every session is in.</summary>
    public const int RoomId = 0;

    /// <summary>How long data1 and data2 are, in bytes.</summary>
    public const int DataLength = 16;

    /// <summary>And how long their base64 is, which is what the buffer is sized for.</summary>
    public const int DataBase64Length = 24;

    /// <summary>The buffer the base64 goes in, terminator included.</summary>
    public const int DataBase64Buffer = 25;

    /// <summary>How many characters customData1 must have before it is decoded at all.</summary>
    public const int CustomData1TextLength = 32;

    /// <summary>The three identity fields the create body fills with one word.</summary>
    public static IReadOnlyList<string> CreateIdentityFields { get; } =
        ["accountId", "deviceUniqueId", "platform"];

    /// <summary>An escaped quote, as in PP196 - these payloads nest inside JSON strings.</summary>
    private const string Q = "\\\"";

    /// <summary>The body that asks PSN for a session.</summary>
    public static string Create(string pushContextId)
    {
        ArgumentNullException.ThrowIfNull(pushContextId);

        return "{\"remotePlaySessions\":["
            + "{\"members\":["
            + $"{{\"accountId\":\"{CreatePlaceholder}\","
            + $"\"deviceUniqueId\":\"{CreatePlaceholder}\","
            + $"\"platform\":\"{CreatePlaceholder}\","
            + "\"pushContexts\":["
            + $"{{\"pushContextId\":\"{pushContextId}\"}}]}}]}}]}}";
    }

    /// <summary>
    /// The payload that starts one - ESCAPED, because it travels inside the envelope's
    /// initialParams string. The account id is written bare here and quoted everywhere else.
    /// </summary>
    public static string StartPayload(long accountId, string sessionId, byte[] data1, byte[] data2)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(data1);
        ArgumentNullException.ThrowIfNull(data2);

        return $"{{{Q}accountId{Q}:{accountId.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}roomId{Q}:{RoomId.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}sessionId{Q}:{Q}{sessionId}{Q},"
            + $"{Q}clientType{Q}:{Q}{ClientType}{Q},"
            + $"{Q}data1{Q}:{Q}{Convert.ToBase64String(data1)}{Q},"
            + $"{Q}data2{Q}:{Q}{Convert.ToBase64String(data2)}{Q}}}";
    }

    /// <summary>The envelope that carries it.</summary>
    public static string StartEnvelope(string deviceUid, string initialParams, string platform)
    {
        ArgumentNullException.ThrowIfNull(deviceUid);
        ArgumentNullException.ThrowIfNull(initialParams);
        ArgumentNullException.ThrowIfNull(platform);

        return "{\"commandDetail\":"
            + "{\"commandType\":\"remotePlay\","
            + $"\"duid\":\"{deviceUid}\","
            + "\"messageDestination\":\"SQS\","
            + $"\"parameters\":{{\"initialParams\":\"{initialParams}\"}},"
            + $"\"platform\":\"{platform}\"}}}}";
    }

    /// <summary>The body that wakes a console - the same two blobs, unescaped this time.</summary>
    public static string WakeupEnvelope(byte[] data1, byte[] data2, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(data1);
        ArgumentNullException.ThrowIfNull(data2);
        ArgumentNullException.ThrowIfNull(sessionId);

        return "{\"data\":"
            + $"{{\"clientType\":\"{ClientType}\","
            + $"\"data1\":\"{Convert.ToBase64String(data1)}\","
            + $"\"data2\":\"{Convert.ToBase64String(data2)}\","
            + $"\"roomId\": {RoomId.ToString(CultureInfo.InvariantCulture)},"
            + $"\"protocolVer\":\"{ProtocolVersion}\","
            + $"\"sessionId\":\"{sessionId}\"}},"
            + "\"dataTypeSuffix\":\"remotePlay\"}";
    }

    /// <summary>
    /// Whether customData1 is long enough to be handed to PP192's decode at all.
    /// </summary>
    public static bool CustomData1IsTheRightLength(string? text)
        => text is not null && text.Length == CustomData1TextLength;
}

/// <summary>
/// PP33: the request bodies' rules where the Qt core states them.
/// </summary>
public static class SessionRequestsSource
{
    /// <summary>Where the templates live.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the reason for hand-rolling these bodies is still recorded.</summary>
    public static bool ItStillSaysWhyTheyAreTemplates(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "Implemented as string templates due to the broken JSON used by the official app",
            StringComparison.Ordinal);
    }

    /// <summary>Whether the create body still sends one word for all three identity fields.</summary>
    public static bool TheCreateBodyStillSaysMeThreeTimes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach (string field in SessionRequests.CreateIdentityFields)
        {
            if (!core.Contains($"\\\"{field}\\\":\\\"{SessionRequests.CreatePlaceholder}\\\"", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the account id is still bare in the start payload and quoted in the two others.
    /// </summary>
    public static bool TheAccountIdIsStillTwoTypes(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        // Bare, in the escaped payload that starts the session: {\\\"accountId\\\":%
        bool bare = core.Contains("{\\\\\\\"accountId\\\\\\\":%", StringComparison.Ordinal);

        // Quoted, in the unescaped message envelope: {\"accountId\":\"%
        bool quoted = core.Contains("{\\\"accountId\\\":\\\"%", StringComparison.Ordinal);

        // And quoted again in the escaped local peer address: {\\\"accountId\\\":\\\"%
        bool quotedEscaped = core.Contains("{\\\\\\\"accountId\\\\\\\":\\\\\\\"%", StringComparison.Ordinal);

        return bare && quoted && quotedEscaped;
    }

    /// <summary>Whether the client still calls itself Windows, and the protocol still ten.</summary>
    public static bool TheClientStillNamesItselfThatWay(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"\\\"clientType\\\":\\\"{SessionRequests.ClientType}\\\"", StringComparison.Ordinal)
            && core.Contains($"\\\"protocolVer\\\":\\\"{SessionRequests.ProtocolVersion}\\\"", StringComparison.Ordinal);
    }

    /// <summary>Whether customData1 is still gated at exactly that length before decoding.</summary>
    public static bool CustomData1IsStillLengthGated(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"if (strlen(custom_data1) != {SessionRequests.CustomData1TextLength})", StringComparison.Ordinal)
            && core.Contains("err = decode_customdata1(", StringComparison.Ordinal);
    }

    /// <summary>Whether the two blobs are still crypto-random and base64 into that exact buffer.</summary>
    public static bool TheBlobsAreStillCryptoRandom(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return CCall.Happens(core, "chiaki_random_bytes_crypt(session->data1, sizeof(session->data1))")
            && CCall.Happens(core, "chiaki_random_bytes_crypt(session->data2, sizeof(session->data2))")
            && core.Contains($"char data1_base64[{SessionRequests.DataBase64Buffer}] = {{0}};", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many fields in the templates block carry a space after their colon. Counted rather than
    /// described, because it is the kind of detail that gets tidied away - and bounded to the
    /// templates so a log message elsewhere cannot pad the number.
    /// </summary>
    public static int SpacedColons(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int from = core.IndexOf("static const char session_create_json_fmt[] =", StringComparison.Ordinal);
        int to = core.IndexOf("typedef enum notification_type_t", StringComparison.Ordinal);
        if (from < 0 || to < from)
            return -1;

        string templates = core[from..to];
        int count = 0;
        int at = 0;

        while ((at = templates.IndexOf("\\\": ", at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at++;
        }

        return count;
    }
}
