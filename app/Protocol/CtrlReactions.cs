using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which pad and keyboard features a session asked for, since the burst depends on them.</summary>
/// <param name="DualSense">connect_info.enable_dualsense.</param>
/// <param name="Keyboard">connect_info.enable_keyboard.</param>
public readonly record struct CtrlFeatures(bool DualSense = false, bool Keyboard = false);

/// <summary>What the control channel has already seen, which is what makes two messages idempotent.</summary>
/// <param name="SessionIdReceived">A session id already arrived; a second is dropped.</param>
/// <param name="SwitchReceived">The stream switch was already acknowledged.</param>
public readonly record struct CtrlSeen(bool SessionIdReceived = false, bool SwitchReceived = false);

/// <summary>How a received session id was judged.</summary>
public enum SessionIdVerdict
{
    /// <summary>Stored as the session id.</summary>
    Accepted,

    /// <summary>Unusable, so a fallback is generated. The session carries on either way.</summary>
    Fallback,

    /// <summary>One had already arrived; this is warned about and dropped.</summary>
    Dropped,
}

/// <summary>
/// PP342, under PP294: what the control channel SENDS when something arrives, and in what order.
///
/// §PP294 names the risk exactly: "a table of message-in, message-out pairs would pass while
/// missing the ordering entirely". So this is not a pair table. Two arrivals produce more than one
/// message, two are idempotent, and one produces a burst whose contents depend on flags the
/// session was built with - and PP297's capture holds the real ordering to hold it against.
///
/// A SESSION ID IS NOT ANSWERED, IT IS ACTED ON. The switch calls the handler and then
/// ctrl_enable_features, so a session id is followed immediately by up to six messages. The capture
/// shows three - two microphone toggles and a display-devices - because it was taken with DualSense
/// and keyboard both off, and those two are the unconditional tail of that function.
///
/// THE MICROPHONE IS TOGGLED TWICE, both times to false. Not a loop and not a typo to tidy: two
/// identical sends, which the capture confirms arrive 108 microseconds apart. A port that sent one
/// would differ from the C in a way no pair table could see.
///
/// A HEARTBEAT IS ANSWERED WHATEVER IT CARRIES. A request with a payload is a warning and the reply
/// goes anyway, empty. The capture's three exchanges are answered in 40, 19 and 18 microseconds, so
/// nothing may sit between the arrival and the reply.
/// </summary>
public static class CtrlReactions
{
    /// <summary>CHIAKI_SESSION_ID_SIZE_MAX, which the length ladder is measured against.</summary>
    public const int SessionIdSizeMax = 80;

    /// <summary>The shortest session id accepted, after the leading byte is dropped.</summary>
    public const int SessionIdMinimum = 24;

    /// <summary>The byte a session id is expected to start with - warned about, never enforced.</summary>
    public const byte SessionIdMarker = 0x4a;

    /// <summary>
    /// What arrives back on the wire when this message arrives, in order. Empty where nothing does.
    /// </summary>
    public static IReadOnlyList<ushort> Answer(ushort received, CtrlFeatures features, CtrlSeen seen)
    {
        // A heartbeat is answered before anything else can happen, whatever it carried.
        if (received == (ushort)CtrlMessage.HeartbeatReq)
            return [(ushort)CtrlMessage.HeartbeatRep];

        // A session id is acted on rather than answered - and only the first one is.
        if (received == (ushort)CtrlMessage.SessionId)
            return seen.SessionIdReceived ? [] : EnableFeatures(features);

        return [];
    }

    /// <summary>
    /// The burst ctrl_enable_features sends, in its own order.
    ///
    /// The two conditional pairs come first, then the unconditional tail. The tail is why a capture
    /// taken with every feature off still shows three messages.
    /// </summary>
    public static IReadOnlyList<ushort> EnableFeatures(CtrlFeatures features)
    {
        var burst = new List<ushort>(6);

        if (features.DualSense)
        {
            burst.Add((ushort)CtrlMessage.EnableDualSenseFeatures);

            // 0x11 has no name in the enum. It is sent with a fixed 16-byte payload and nothing
            // in the tree says what it is, which is worth leaving visible rather than naming.
            burst.Add(0x11);
        }

        if (features.Keyboard)
        {
            burst.Add((ushort)CtrlMessage.KeyboardEnable);
            burst.Add((ushort)CtrlMessage.KeyboardEnableToggle);
        }

        // Twice, both false. See the note on the class.
        burst.Add((ushort)CtrlMessage.MicToggle);
        burst.Add((ushort)CtrlMessage.MicToggle);

        burst.Add((ushort)CtrlMessage.DisplayDevices);

        return burst;
    }

    /// <summary>
    /// How a session id payload is judged, by the ladder in ctrl_message_received_session_id.
    ///
    /// EVERY FAILURE IS A FALLBACK AND NOT AN ERROR. An unusable session id does not end the
    /// session; a generated one is substituted and the connect carries on. A port that failed here
    /// would refuse consoles the C connects to.
    /// </summary>
    public static SessionIdVerdict JudgeSessionId(ReadOnlySpan<byte> payload, CtrlSeen seen)
    {
        if (seen.SessionIdReceived)
            return SessionIdVerdict.Dropped;

        if (payload.Length < 2)
            return SessionIdVerdict.Fallback;

        // payload[0] is checked, warned about, and then not acted on - the id is used either way.
        // The first byte is dropped as a length regardless of what it said.
        ReadOnlySpan<byte> id = payload[1..];

        // Both bounds are measured AFTER the drop, and the upper one leaves room for the NUL that
        // is written at id.Length.
        if (id.Length >= SessionIdSizeMax - 1)
            return SessionIdVerdict.Fallback;

        if (id.Length < SessionIdMinimum)
            return SessionIdVerdict.Fallback;

        foreach (byte b in id)
        {
            bool alphanumeric =
                (b >= (byte)'a' && b <= (byte)'z')
                || (b >= (byte)'A' && b <= (byte)'Z')
                || (b >= (byte)'0' && b <= (byte)'9');

            if (!alphanumeric)
                return SessionIdVerdict.Fallback;
        }

        return SessionIdVerdict.Accepted;
    }

    /// <summary>
    /// Whether the stream switch acknowledgement does anything, which it does exactly once.
    /// </summary>
    public static bool SwitchIsActedOn(CtrlSeen seen) => !seen.SwitchReceived;
}

/// <summary>
/// The control message types this port names, by the value ctrl.c gives them. Held against that
/// file by <see cref="CtrlMessageSecrets.DeclaredIn"/>, which reads the same enum.
/// </summary>
public enum CtrlMessage : ushort
{
    DisplayA = 0x1,
    LoginPinReq = 0x4,
    Login = 0x5,
    EnableDualSenseFeatures = 0x13,
    GoHome = 0x14,
    DisplayB = 0x16,
    KeyboardEnable = 0xd,
    KeyboardEnableToggle = 0x20,
    KeyboardOpen = 0x21,
    KeyboardCloseRemote = 0x22,
    KeyboardTextChangeReq = 0x23,
    KeyboardTextChangeRes = 0x24,
    KeyboardCloseReq = 0x25,
    MicConnect = 0x30,
    SessionId = 0x33,
    SwitchToStreamConnection = 0x34,
    MicToggle = 0x36,
    GotoBed = 0x50,
    DisplayDevices = 0x910,
    HeartbeatReq = 0xfe,
    LoginPinRep = 0x8004,
    HeartbeatRep = 0x1fe,
}

/// <summary>
/// PP342: the reactions held against ctrl.c, for the parts PP297's capture cannot reach - a console
/// that asked for no PIN, sent one session id and never a malformed one.
/// </summary>
public static class CtrlReactionsSource
{
    /// <summary>Where the switch and the burst live.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Whether a session id still triggers the feature burst from inside the switch.</summary>
    public static bool ASessionIdStillEnablesFeatures(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int arm = core.IndexOf("case CTRL_MESSAGE_TYPE_SESSION_ID:", StringComparison.Ordinal);
        if (arm < 0)
            return false;

        int handler = core.IndexOf("ctrl_message_received_session_id(ctrl", arm, StringComparison.Ordinal);
        int enable = core.IndexOf("ctrl_enable_features(ctrl)", arm, StringComparison.Ordinal);
        int next = core.IndexOf("break;", arm, StringComparison.Ordinal);

        return handler > arm && enable > handler && next > enable;
    }

    /// <summary>
    /// Whether the microphone is still toggled twice, and still to false both times.
    ///
    /// PP383: matched WITHOUT the trailing semicolon. This asked for
    /// <c>ctrl_message_toggle_microphone(ctrl, false);</c> and went red when the two calls were
    /// wrapped in the guard that reads their result - a check on punctuation rather than on the
    /// two sends, failing on a change that kept both of them exactly where they were.
    /// </summary>
    public static bool TheMicrophoneIsStillToggledTwice(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        const string Call = "ctrl_message_toggle_microphone(ctrl, false)";

        int first = core.IndexOf(Call, StringComparison.Ordinal);
        if (first < 0)
            return false;

        int second = core.IndexOf(Call, first + Call.Length, StringComparison.Ordinal);

        return second > first;
    }

    /// <summary>
    /// Whether the burst still ends with display-devices, after the two conditional pairs.
    ///
    /// PP383: without the semicolon, for the reason given on the check above - both readers keyed
    /// on punctuation, and both went red on a change that moved neither send.
    /// </summary>
    public static bool TheBurstStillEndsWithDisplayDevices(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int mic = core.IndexOf("ctrl_message_toggle_microphone(ctrl, false)", StringComparison.Ordinal);
        int display = core.IndexOf(
            "CTRL_MESSAGE_TYPE_DISPLAY_DEVICES, display", StringComparison.Ordinal);

        return mic > 0 && display > mic;
    }

    /// <summary>
    /// One handler's body, or null.
    ///
    /// Through <see cref="CFunction"/>, which skips the prototype ctrl.c declares at the top of the
    /// file for every static handler - the trap this walked into before PP343 gave the reader a name
    /// that says what it reads.
    /// </summary>
    public static string? HandlerBody(string filePath, string handler)
        => CFunction.BodyIn(filePath, handler);

    /// <summary>
    /// Whether a heartbeat is still answered whatever its payload carried.
    ///
    /// The non-empty payload is a warning and the reply is not inside any branch, so a `return`
    /// appearing in this body is the change that matters.
    /// </summary>
    public static bool AHeartbeatIsStillAnsweredRegardless(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return body.Contains("CHIAKI_LOGW", StringComparison.Ordinal)
            && body.Contains("CTRL_MESSAGE_TYPE_HEARTBEAT_REP, NULL, 0", StringComparison.Ordinal)
            && !body.Contains("return", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether an unusable session id still falls back on every rung rather than failing.
    ///
    /// Four rungs, four fallbacks: shorter than two, longer than the maximum, under the minimum,
    /// and a character outside a-zA-Z0-9.
    /// </summary>
    public static bool AnUnusableSessionIdStillFallsBack(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // PP385: counted through the guard the four rungs now go through, not through the bare
        // call. This asked for ctrl_message_set_fallback_session_id(ctrl) by name and went red when
        // the four were wrapped in the macro that reads their result - the third reader in this
        // file keyed on how a call is spelled rather than on whether it happens.
        var fallbacks = 0;
        const string Call = "CTRL_FALLBACK_SESSION_ID(ctrl)";

        for (int at = body.IndexOf(Call, StringComparison.Ordinal);
             at >= 0;
             at = body.IndexOf(Call, at + Call.Length, StringComparison.Ordinal))
        {
            fallbacks++;
        }

        return fallbacks == 4;
    }
}
