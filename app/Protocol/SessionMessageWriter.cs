using System.Globalization;
using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: writing a session message, which is done with format strings and not with a JSON library.
///
/// The core says why, in as many words: "Since the official remote play app doesn't send valid JSON
/// half the time, we can't use a proper JSON library to serialize the message." That is not a
/// shortcut - it is a REQUIREMENT, and the field that makes it one is localPeerAddr. When there is
/// no local peer address the field's value must be the EMPTY STRING, producing
/// <c>"localPeerAddr":,</c> - which is not JSON at all. Sony's app does it, so the console expects
/// it, so a serialiser that emits well-formed output is the one that gets refused. PP191 already
/// ported the other half of this: the reader that REPAIRS the same hole on the way back in.
///
/// Three more things this writer does that reading the parser would not predict:
///
///   THE ROUTE MAC IS ALWAYS EMPTY. The field is written from a one-byte buffer holding nothing but
///   the terminator, so this client never sends its own MAC - every offer it makes carries "". And
///   the reader will not parse a MAC of any length but seventeen (PP194), so the field is write-only
///   and read-never in both directions at once. A port that helpfully filled in the real adapter's
///   address would be the first client to put it on the wire.
///
///   THE TWO ENUMS FAIL DIFFERENTLY. An undefined candidate type ABORTS the whole serialisation
///   with INVALID_DATA; an undefined action is written out as the word "UNKNOWN", which the parser
///   never compares against and which therefore travels as a valid-looking message nothing can read
///   back. Same file, same shape of switch, two opposite answers to "what if it is none of these".
///
///   AN ACK IS A DIFFERENT SERIALISER. Acks go through a second function whose connRequest is the
///   literal <c>{}</c> rather than an empty or omitted object - so the shape is always present and
///   always well-formed, which is the one part of this message that is.
///
/// Everything here is JSON-ESCAPED, because the whole message is embedded in the payload string of
/// the envelope - see <see cref="Envelope"/>.
/// </summary>
public static class SessionMessageWriter
{
    /// <summary>The platform this client calls itself in a local peer address.</summary>
    public const string ClientPlatform = "REMOTE_PLAY";

    /// <summary>
    /// The route MAC this client sends: NOTHING. Written from a one-byte buffer holding only the
    /// terminator - see the class note.
    /// </summary>
    public const string RouteMacSent = "";

    /// <summary>The connRequest an ack carries, which is the literal empty object.</summary>
    public const string AckConnectionRequest = "{}";

    /// <summary>The action word written for anything the switch does not name.</summary>
    public const string UnknownAction = "UNKNOWN";

    /// <summary>The words the writer produces for each action.</summary>
    public static IReadOnlyDictionary<SessionMessageAction, string> ActionWords { get; } =
        new Dictionary<SessionMessageAction, string>
        {
            [SessionMessageAction.Offer] = "OFFER",
            [SessionMessageAction.Result] = "RESULT",
            [SessionMessageAction.Accept] = "ACCEPT",
            [SessionMessageAction.Terminate] = "TERMINATE",
        };

    /// <summary>
    /// The word an action is written as, falling through to <see cref="UnknownAction"/> - a word
    /// the parser never compares against, so a message carrying it is readable by nobody.
    /// </summary>
    public static string WordFor(SessionMessageAction action)
        => ActionWords.TryGetValue(action, out string? word) ? word : UnknownAction;

    /// <summary>An escaped quote, which is how every string in these payloads is delimited.</summary>
    private const string Q = "\\\"";

    /// <summary>One local peer address, or the empty string when there is none to send.</summary>
    public static string LocalPeerAddress(long accountId, string platform = ClientPlatform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return $"{{{Q}accountId{Q}:{Q}{accountId.ToString(CultureInfo.InvariantCulture)}{Q},"
            + $"{Q}platform{Q}:{Q}{platform}{Q}}}";
    }

    /// <summary>
    /// One candidate, or null when its type is not one of the four - which ABORTS the whole
    /// serialisation in the core rather than falling back the way the reader does.
    /// </summary>
    public static string? Candidate(Candidate candidate)
    {
        if (!CandidateReader.Written.TryGetValue(candidate.Type, out string? type))
            return null;

        return $"{{{Q}type{Q}:{Q}{type}{Q},"
            + $"{Q}addr{Q}:{Q}{candidate.Address}{Q},"
            + $"{Q}mappedAddr{Q}:{Q}{candidate.MappedAddress}{Q},"
            + $"{Q}port{Q}:{candidate.Port.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}mappedPort{Q}:{candidate.MappedPort.ToString(CultureInfo.InvariantCulture)}}}";
    }

    /// <summary>
    /// The candidate array, or null when any one of them has an undefined type - the core's loop
    /// jumps out of the whole serialisation on the first, so there is no partial array to send.
    /// </summary>
    public static string? Candidates(IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var text = new StringBuilder("[");
        for (int i = 0; i < candidates.Count; i++)
        {
            string? one = Candidate(candidates[i]);
            if (one is null)
                return null;

            if (i > 0)
                text.Append(',');

            text.Append(one);
        }

        return text.Append(']').ToString();
    }

    /// <summary>
    /// One connRequest, or null when a candidate's type is undefined.
    ///
    /// <paramref name="localPeerAddress"/> is written RAW: pass the empty string for "no local peer
    /// address" and the result is deliberately broken JSON, which is what the console is expecting.
    /// The MAC is not a parameter at all, because the core never sends one.
    /// </summary>
    public static string? ConnectionRequest(
        uint sid,
        uint peerSid,
        byte[] skey,
        byte natType,
        IReadOnlyList<Candidate> candidates,
        string localPeerAddress,
        byte[] localHashedId)
    {
        ArgumentNullException.ThrowIfNull(skey);
        ArgumentNullException.ThrowIfNull(localHashedId);
        ArgumentNullException.ThrowIfNull(localPeerAddress);

        string? candidateArray = Candidates(candidates);
        if (candidateArray is null)
            return null;

        return $"{{{Q}sid{Q}:{sid.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}peerSid{Q}:{peerSid.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}skey{Q}:{Q}{Convert.ToBase64String(skey)}{Q},"
            + $"{Q}natType{Q}:{natType.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}candidate{Q}:{candidateArray},"
            + $"{Q}defaultRouteMacAddr{Q}:{Q}{RouteMacSent}{Q},"
            + $"{Q}localPeerAddr{Q}:{localPeerAddress},"
            + $"{Q}localHashedId{Q}:{Q}{Convert.ToBase64String(localHashedId)}{Q}}}";
    }

    /// <summary>The message around a connRequest, escaped and ready to embed in the envelope.</summary>
    public static string Message(SessionMessageAction action, int requestId, int error, string connectionRequest)
    {
        ArgumentNullException.ThrowIfNull(connectionRequest);

        return $"{{{Q}action{Q}:{Q}{WordFor(action)}{Q},"
            + $"{Q}reqId{Q}:{requestId.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}error{Q}:{error.ToString(CultureInfo.InvariantCulture)},"
            + $"{Q}connRequest{Q}:{connectionRequest}}}";
    }

    /// <summary>An ack: the same message, with the literal empty object where a request would go.</summary>
    public static string ShortMessage(SessionMessageAction action, int requestId, int error)
        => Message(action, requestId, error, AckConnectionRequest);

    /// <summary>
    /// The envelope the message travels in, where the body goes into the PAYLOAD STRING after
    /// "body=" - which is why everything above is escaped, and what PP191 reads back out.
    /// </summary>
    public static string Envelope(string body, long accountId, string deviceUniqueId, string platform)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(deviceUniqueId);
        ArgumentNullException.ThrowIfNull(platform);

        return "{\"channel\":\"remote_play:1\","
            + $"\"payload\":\"ver=1.0, type=text, body={body}\","
            + "\"to\":["
            + $"{{\"accountId\":\"{accountId.ToString(CultureInfo.InvariantCulture)}\","
            + $"\"deviceUniqueId\":\"{deviceUniqueId}\","
            + $"\"platform\":\"{platform}\"}}]}}";
    }
}

/// <summary>
/// PP33: the writer's rules where the Qt core states them - as FORMAT STRINGS, which is a shape a
/// test can compare against directly rather than a control flow it has to infer.
/// </summary>
public static class SessionMessageWriterSource
{
    /// <summary>Where the message is serialised.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the reason for hand-rolling the JSON is still recorded.</summary>
    public static bool ItStillSaysWhyThereIsNoJsonLibrary(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "Since the official remote play app doesn't send valid JSON", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether localPeerAddr is still documented as needing an empty string, broken JSON and all.
    /// </summary>
    public static bool TheEmptyPeerAddressIsStillDeliberate(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("Needs to be an empty string if local peer address is not submitted", StringComparison.Ordinal)
            && core.Contains("This leads to broken JSON", StringComparison.Ordinal);
    }

    /// <summary>Whether the MAC is still written from a buffer that can only hold nothing.</summary>
    public static bool TheMacIsStillAlwaysEmpty(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("char mac_addr[1] = { '\\0' };", StringComparison.Ordinal);
    }

    /// <summary>Whether an undefined candidate type still aborts the serialisation.</summary>
    public static bool AnUndefinedCandidateTypeStillAborts(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("Undefined candidate type %d", StringComparison.Ordinal)
            && core.Contains("err = CHIAKI_ERR_INVALID_DATA;", StringComparison.Ordinal);
    }

    /// <summary>And whether an undefined action is still written out as a word instead.</summary>
    public static bool AnUndefinedActionIsStillWritten(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"action_str = \"{SessionMessageWriter.UnknownAction}\";", StringComparison.Ordinal);
    }

    /// <summary>Whether an ack's connRequest is still the literal empty object.</summary>
    public static bool AnAckStillCarriesAnEmptyObject(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("char connreq_json[3] = { '{', '}', '\\0' };", StringComparison.Ordinal);
    }

    /// <summary>
    /// The keys of the connRequest format string, in the order the core writes them - taken from
    /// the format string itself, so a field added, dropped or reordered upstream turns this red.
    /// </summary>
    public static IReadOnlyList<string> ConnectionRequestKeys { get; } =
    [
        "sid", "peerSid", "skey", "natType", "candidate",
        "defaultRouteMacAddr", "localPeerAddr", "localHashedId",
    ];

    /// <summary>Whether the connRequest format still names those keys in that order.</summary>
    public static bool TheRequestKeysAreStillInOrder(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf("session_connrequest_fmt[] =", StringComparison.Ordinal);
        if (at < 0)
            return false;

        // The declaration ends at its terminating semicolon, which is the end of the string literal.
        int end = core.IndexOf(";", at, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string format = core[at..end];
        int cursor = 0;
        foreach (string key in ConnectionRequestKeys)
        {
            int found = format.IndexOf($"\\\\\\\"{key}\\\\\\\":", cursor, StringComparison.Ordinal);
            if (found < 0)
                return false;

            cursor = found + 1;
        }

        return true;
    }
}
