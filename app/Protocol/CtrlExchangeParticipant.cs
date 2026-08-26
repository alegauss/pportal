using System.Globalization;
using System.Text;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23's next module: PP342's table, replayed against PP297's recording.
///
/// Three shipped tasks that had never been joined. PP293 built the replay harness, PP297 captured a
/// real PS5 exchange, and PP342 modelled what the control channel sends when something arrives - and
/// the harness had only ever been fed recordings a test wrote itself. A participant that agrees with
/// a synthetic recording agrees with the test that made it; this is the first time managed code is
/// asked whether it would have said what a console actually heard.
///
/// IT ANSWERS FROM THE TABLE AND NOTHING ELSE. Every reply comes from
/// <see cref="CtrlReactions.Answer"/>, so a divergence is a defect in the model rather than in a
/// second implementation written to pass. The payloads are the one thing the table does not carry -
/// it answers in message types - so they live here, read off ctrl.c and named.
///
/// THE CAPTURE WAS TAKEN WITH BOTH FEATURES OFF, which is why the session-id burst in it is three
/// messages rather than seven. That is a property of the recording and not of the model, so the
/// features are a parameter and the recording's own value is passed at the call site.
///
/// THE SESSION CHANNEL IS NOT THIS PARTICIPANT'S. The recording holds an HTTP request and its
/// answer, and the ctrl channel is a different conversation on a different socket - so a session
/// entry is received and answered with nothing, which is what the ctrl channel does about it.
/// </summary>
public sealed class CtrlExchangeParticipant(CtrlFeatures features) : IExchangeParticipant
{
    private CtrlSeen seen;

    /// <summary>What the console has already been told, for a caller inspecting the end state.</summary>
    public CtrlSeen Seen => seen;

    /// <summary>
    /// The payload each message in the burst carries, read off ctrl.c.
    ///
    /// The three the capture exercises are the microphone toggle and the display-devices request;
    /// the rest are here because the burst can send them and a table with holes in it would answer
    /// a keyboard session with an empty payload rather than with a failure.
    /// </summary>
    public static IReadOnlyDictionary<ushort, byte[]> Payloads { get; } = new Dictionary<ushort, byte[]>
    {
        // ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_HEARTBEAT_REP, NULL, 0)
        [(ushort)CtrlMessage.HeartbeatRep] = [],

        // uint8_t toggle[0x4] = {0, 1, 1, 89}; muted would zero the third, and the burst passes false.
        [(ushort)CtrlMessage.MicToggle] = [0x00, 0x01, 0x01, 0x59],

        // uint8_t display[0x4] = {0, 0, 0, 0}
        [(ushort)CtrlMessage.DisplayDevices] = [0x00, 0x00, 0x00, 0x00],

        // const uint8_t enable[3] = { 0x00, 0x40, 0x00 }
        [(ushort)CtrlMessage.EnableDualSenseFeatures] = [0x00, 0x40, 0x00],

        // PP383: fifteen initialisers for a sixteen-byte array, so the last byte is an implicit zero.
        [0x11] =
        [
            0xa0, 0xab, 0x51, 0xbd, 0xd1, 0x7e, 0x00, 0x00,
            0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00,
        ],

        // uint8_t signature[0x10] = { 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x05, 0xAE, 0, ... }
        [(ushort)CtrlMessage.KeyboardEnable] =
        [
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x05, 0xAE,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ],

        // uint8_t enable = 1
        [(ushort)CtrlMessage.KeyboardEnableToggle] = [0x01],
    };

    /// <summary>
    /// One thing the console said, and everything this would say back.
    ///
    /// A payload that is not in the table is a message the burst can produce and nobody wrote down,
    /// which is a gap rather than an empty answer - so it throws rather than rendering nothing.
    /// </summary>
    public IReadOnlyList<string> Receive(string channel, string payload)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        // Not this conversation. See the note on the class.
        if (channel != ChiakiNg.Native.ChiakiMessageTap.CtrlChannel)
            return [];

        ushort? received = TypeOf(payload);
        if (received is null)
            return [];

        IReadOnlyList<ushort> answers = CtrlReactions.Answer(received.Value, features, seen);

        // The state moves after the answer is computed, because the answer is what a first session
        // id produces and a second produces nothing.
        if (received.Value == (ushort)CtrlMessage.SessionId)
            seen = seen with { SessionIdReceived = true };

        if (received.Value == (ushort)CtrlMessage.SwitchToStreamConnection)
            seen = seen with { SwitchReceived = true };

        return [.. answers.Select(Render)];
    }

    /// <summary>
    /// The type at the front of a recorded ctrl payload, or null where the text is not one.
    ///
    /// Four hex digits and then a space, which is what <c>ExchangeRecorder.Render</c> writes.
    /// </summary>
    public static ushort? TypeOf(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length < 4)
            return null;

        return ushort.TryParse(
            payload.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort type)
            ? type
            : null;
    }

    /// <summary>
    /// One message as the recording would have written it.
    ///
    /// Through the recorder's own shape - four hex digits, a space, dash-separated bytes - because a
    /// participant that rendered differently would diverge on formatting and report it as protocol.
    /// </summary>
    public static string Render(ushort type)
    {
        if (!Payloads.TryGetValue(type, out byte[]? payload))
        {
            throw new KeyNotFoundException(
                $"no payload is written down for ctrl message {type:x4}, so a replay of it would "
                + "compare an invented one against the console's");
        }

        var text = new StringBuilder(8 + (payload.Length * 3));
        text.Append(CultureInfo.InvariantCulture, $"{type:x4} ");

        for (var i = 0; i < payload.Length; i++)
        {
            if (i > 0)
                text.Append('-');

            text.Append(CultureInfo.InvariantCulture, $"{payload[i]:x2}");
        }

        return text.ToString();
    }
}
