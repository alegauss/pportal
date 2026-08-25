using System.Diagnostics;
using System.Globalization;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP326: the wire between PP323's tap and PP297's recording, which were both built and never
/// joined.
///
/// Install this, run a session, and what comes out is a recording the replay can be driven from.
/// Everything it does is translation - the tap speaks direction, channel, type and bytes; the
/// recording speaks offset, direction, channel and text - and the translation has two decisions in
/// it, both about the control channel.
///
/// RENDERING, and why it is dashes
/// -------------------------------
/// The recording is text and the ctrl channel is bytes, so the bytes have to be written somehow -
/// and <see cref="ExchangeRecording.Add"/> redacts what it is handed, so the rendering has to
/// survive rules written for log lines. Both obvious ones do not. Continuous hex of eight bytes is
/// sixteen hex characters and LongHexPattern takes any run of sixteen or more; space-separated
/// pairs are the exact shape HexdumpRowPattern was written for. Colons are worse still - three or
/// more colon-separated hex groups is what Ipv6Pattern matches, so a ctrl payload would come out as
/// a redacted IPv6 address.
///
/// Dash-separated pairs match none of the ten: the word boundaries break a long-hex run, there is
/// no space for the hexdump rule, no colon for the IPv6 one, and no 8-4-4-4-12 shape for the UUID
/// one. It is also legible, which PP297 asks for by name - a person diffs two recordings as often
/// as a test does.
///
/// WHAT MAY BE RECORDED AT ALL is <see cref="CtrlMessageSecrets"/>, and it is the same argument
/// PP325 made one channel over: the payload goes because of what the message IS, not because of
/// what its bytes look like. Nothing about the text someone typed on a console keyboard looks like
/// a secret.
///
/// The TYPE goes in the payload text rather than into a field of its own. ExchangeEntry has no
/// type, and adding one is a format version and a change to the replay for something the payload
/// can carry: for the control channel the type is part of what was said.
///
/// THREADS. The tap fires on the ctrl thread and on the session thread, neither of which this
/// created, so <see cref="ExchangeRecording"/> is guarded here rather than left to be discovered.
/// </summary>
public sealed class ExchangeRecorder : IDisposable
{
    private readonly ExchangeRecording recording = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Lock guard = new();
    private ChiakiMessageTap? tap;

    private ExchangeRecorder()
    {
    }

    /// <summary>
    /// Starts recording, replacing any tap already installed.
    ///
    /// The clock starts here and not at the first message, so the offset of the first entry is the
    /// gap between turning recording on and anything happening - which is the one reading that says
    /// whether a recording missed the start of the exchange.
    /// </summary>
    public static ExchangeRecorder Start()
    {
        var recorder = new ExchangeRecorder();
        recorder.tap = ChiakiMessageTap.Install(recorder.Record);
        return recorder;
    }

    /// <summary>What has been recorded so far. Safe to read while a session is running.</summary>
    public ExchangeRecording Recording
    {
        get
        {
            lock (guard)
                return recording;
        }
    }

    /// <summary>The recording as text, ready to be written to a file.</summary>
    public string Write()
    {
        lock (guard)
            return recording.Write();
    }

    /// <summary>Stops recording. Idempotent.</summary>
    public void Dispose()
    {
        tap?.Dispose();
        tap = null;
    }

    /// <summary>
    /// One tapped message, translated and stored.
    ///
    /// Called on a thread this did not create. Nothing here throws on its own, and
    /// <see cref="ChiakiMessageTap"/> swallows what does - a recorder that took a session down
    /// would be worse than a recording with a gap in it.
    /// </summary>
    private void Record(TappedMessage message)
    {
        string payload = Render(message);

        lock (guard)
        {
            recording.Add(
                clock.ElapsedTicks / (Stopwatch.Frequency / 1_000_000),
                message.Direction == ExchangeTapDirection.Sent
                    ? ExchangeDirection.Sent
                    : ExchangeDirection.Received,
                message.Channel,
                payload);
        }
    }

    /// <summary>
    /// A tapped message as the text the recording stores.
    ///
    /// The session channel is an HTTP head and goes in as itself - Latin-1, so every byte maps to
    /// exactly one character and back with nothing replaced. UTF-8 would turn a byte that is not
    /// valid UTF-8 into U+FFFD, and a recording that cannot round-trip its own bytes is not one.
    /// PP325 takes the two secret headers out of it on the way in.
    /// </summary>
    public static string Render(TappedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message.Channel);

        if (message.Channel == ChiakiMessageTap.SessionChannel)
            return Latin1.GetString(message.Payload);

        // Everything else is the control channel: the type, then the bytes or the marker.
        string body = CtrlMessageSecrets.MayRecord(message.Type)
            ? Dashed(message.Payload)
            : CtrlMessageSecrets.Marker;

        return $"{message.Type:x4} {body}";
    }

    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>
    /// Bytes as dash-separated pairs, which is the rendering no sanitiser rule matches. See the
    /// note on the class for the three that do match, and why each was rejected.
    /// </summary>
    private static string Dashed(byte[] payload)
    {
        if (payload.Length == 0)
            return "";

        var text = new StringBuilder(payload.Length * 3);
        foreach (byte b in payload)
        {
            if (text.Length > 0)
                text.Append('-');

            text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}
