using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: what one session message asks for, as the flags a caller waits on.
///
/// THE VALUES SKIP ONE. Offer is 1, and Result is 1&lt;&lt;2 - four. Nothing is 1&lt;&lt;1, and the gap is
/// not a typo to tidy: <c>wait_for_session_message</c> takes a mask, so a port that closed the gap
/// would give its Accept the value the core calls Result. A message the console sent as a RESULT
/// would then be read as an ACCEPT, and the handshake would proceed on the wrong branch with every
/// field present and plausible.
/// </summary>
[Flags]
public enum SessionMessageAction
{
    /// <summary>Zero, so it is in no mask.</summary>
    Unknown = 0,

    /// <summary>One - and NOT 1&lt;&lt;0 spelled differently, because what follows skips a bit.</summary>
    Offer = 1,

    /// <summary>Four. The bit worth two does not exist.</summary>
    Result = 1 << 2,

    Accept = 1 << 3,
    Terminate = 1 << 4,
}

/// <summary>
/// PP33: the session message envelope, including the JSON the sender gets wrong.
///
/// Three layers between the notification and the message, and each one is a place a port stops:
///
/// 1. THE PAYLOAD IS AT A POINTER, not at a key: /body/data/sessionMessage/payload, four levels
///    down, and it is a STRING rather than an object.
///
/// 2. THAT STRING IS NOT JSON. It carries a "body=" marker and the JSON begins after it, so a
///    parser handed the payload gets nothing and says the payload is malformed - which is true and
///    unhelpful.
///
/// 3. AND THEN THE JSON IS SOMETIMES INVALID. When the console has no local peer address, PSN
///    sends `"localPeerAddr":` followed immediately by a comma - a key with no value at all. Every
///    conforming parser refuses it. The core repairs that one field by inserting an empty object
///    and parses the result.
///
/// The third is worth separating from PP183's decision, because they look alike and are not. That
/// one refused to make the LEXER lenient - to be bug-compatible with json-c for inputs Sony does
/// not send. This is the opposite case: a specific malformed field that Sony DOES send, repaired
/// at a named place rather than by loosening what counts as JSON.
/// </summary>
public static class SessionMessageEnvelope
{
    /// <summary>Where the payload sits in the notification.</summary>
    public const string PayloadPointer = "/body/data/sessionMessage/payload";

    /// <summary>What the JSON inside the payload string begins after.</summary>
    public const string BodyMarker = "body=";

    /// <summary>The field PSN sometimes sends with no value at all.</summary>
    public const string PeerAddrKey = "\"localPeerAddr\":";

    /// <summary>Each action's word, as the message spells it.</summary>
    public static IReadOnlyDictionary<SessionMessageAction, string> Actions { get; } =
        new Dictionary<SessionMessageAction, string>
        {
            [SessionMessageAction.Offer] = "OFFER",
            [SessionMessageAction.Result] = "RESULT",
            [SessionMessageAction.Accept] = "ACCEPT",
            [SessionMessageAction.Terminate] = "TERMINATE",
        };

    /// <summary>The action a word names, or <see cref="SessionMessageAction.Unknown"/>.</summary>
    public static SessionMessageAction ActionOf(string? word)
    {
        foreach ((SessionMessageAction action, string name) in Actions)
        {
            if (string.Equals(word, name, StringComparison.Ordinal))
                return action;
        }

        return SessionMessageAction.Unknown;
    }

    /// <summary>Whether a message is one a caller waiting on <paramref name="mask"/> wants.</summary>
    public static bool Matches(SessionMessageAction action, SessionMessageAction mask)
        => action != SessionMessageAction.Unknown && (action & mask) != 0;

    /// <summary>
    /// The JSON inside a payload string, or null when there is no body marker.
    ///
    /// Everything before "body=" is discarded, which is what the core does - it does not parse the
    /// prefix or check it, so neither does this.
    /// </summary>
    public static string? JsonInPayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        int at = payload.IndexOf(BodyMarker, StringComparison.Ordinal);
        return at < 0 ? null : payload[(at + BodyMarker.Length)..];
    }

    /// <summary>
    /// PSN's one malformed field, repaired.
    ///
    /// Only that field, and only when it has no value: a message without the key is returned
    /// untouched, and so is one whose value is already an object. Narrow on purpose - a repair
    /// that ran over every empty value would be a lenient parser wearing a fix's name, and PP183
    /// decided against those.
    /// </summary>
    public static string RepairPeerAddr(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        int key = json.IndexOf(PeerAddrKey, StringComparison.Ordinal);
        if (key < 0)
            return json;

        int valueAt = key + PeerAddrKey.Length;
        if (valueAt < json.Length && json[valueAt] == '{')
            return json;

        return string.Concat(json.AsSpan(0, valueAt), "{}", json.AsSpan(valueAt));
    }

    /// <summary>The two steps together: the payload string in, parseable JSON out.</summary>
    public static string? JsonFor(string payload)
    {
        string? json = JsonInPayload(payload);
        return json is null ? null : RepairPeerAddr(json);
    }
}

/// <summary>
/// PP33: the session message where the Qt core states it.
/// </summary>
public static class SessionMessageSource
{
    /// <summary>Where the envelope is read and repaired.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether the action values still skip a bit.</summary>
    public static bool TheActionsStillSkipABit(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("SESSION_MESSAGE_ACTION_OFFER = 1,", StringComparison.Ordinal)
            && core.Contains("SESSION_MESSAGE_ACTION_RESULT = 1 << 2,", StringComparison.Ordinal)
            && !core.Contains("SESSION_MESSAGE_ACTION_RESULT = 1 << 1,", StringComparison.Ordinal);
    }

    /// <summary>Whether the payload is still four levels down and still a string.</summary>
    public static bool ThePayloadIsStillAtThatPointer(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"\"{SessionMessageEnvelope.PayloadPointer}\"", StringComparison.Ordinal)
            && core.Contains("json_object_is_type(payload_json, json_type_string)", StringComparison.Ordinal);
    }

    /// <summary>Whether the JSON still starts after a marker rather than at the payload.</summary>
    public static bool TheJsonStillStartsAfterAMarker(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains($"strstr(payload_str, \"{SessionMessageEnvelope.BodyMarker}\")", StringComparison.Ordinal);
    }

    /// <summary>Whether the sender still omits that one value, and the core still repairs it.</summary>
    public static bool ThePeerAddrIsStillRepaired(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("peeraddr_key = \"\\\"localPeerAddr\\\":\"", StringComparison.Ordinal)
            && core.Contains("// Insert empty object as key for localPeerAddr key", StringComparison.Ordinal);
    }
}
