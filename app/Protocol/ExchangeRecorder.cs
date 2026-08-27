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

    /// <summary>
    /// Stops recording and writes what it has, answering what happened rather than throwing.
    ///
    /// The caller is an application on its way out, after the user has already closed it, so a
    /// throw here would replace whatever they did last with a crash dialog - and the thing that
    /// failed is a diagnostic, which is the one thing that must never be why a session ends badly.
    /// The sentence comes back so the caller can print it; this decides nothing about where.
    ///
    /// STOPPED BEFORE THE WRITE, so a message arriving on the ctrl thread cannot land in the
    /// recording halfway through serialising it.
    /// </summary>
    /// <returns>True where the file was written.</returns>
    public bool TryWriteTo(string path, out string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Dispose();

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, Write());

            message = $"{Recording.Entries.Count} entries written to {path}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or PathTooLongException)
        {
            message = $"could not write {path}: {ex.Message}";
            return false;
        }
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
                Microseconds(clock.ElapsedTicks, Stopwatch.Frequency),
                message.Direction == ExchangeTapDirection.Sent
                    ? ExchangeDirection.Sent
                    : ExchangeDirection.Received,
                message.Channel,
                payload);
        }
    }

    /// <summary>
    /// PP328: how long the clock has run, in microseconds, WITHOUT rounding the rate first.
    ///
    /// The obvious spelling divides the tick rate down to ticks-per-microsecond and then divides
    /// the ticks by that, and the inner division is integer. On the 10 MHz counter Windows has
    /// reported since Windows 8 it comes to exactly 10 and the arithmetic is right by luck.
    /// QueryPerformanceFrequency promises a FIXED rate and not that one: a VM on the ACPI
    /// power-management timer reports 3,579,545 Hz, which rounds to 3, and every offset then reads
    /// about 19 percent long. A counter under 1 MHz would round the divisor to nought.
    ///
    /// Multiplying first fixes it: the rate is never rounded on its own, and the truncation happens
    /// once at the end, where the loss is the unit the recording is written in rather than a scale
    /// error in every entry.
    ///
    /// A FUNCTION OF TWO NUMBERS and not of the clock, because a test cannot move
    /// Stopwatch.Frequency. Taking the rate as an argument is the only way the case this exists for
    /// - a counter that is not a whole number of ticks per microsecond - can be asserted at all on
    /// a machine whose counter is.
    ///
    /// The multiply cannot overflow at any rate a session runs at: a 10 MHz counter reaches
    /// long.MaxValue after about 29 000 years of ticks times a million.
    /// </summary>
    public static long Microseconds(long ticks, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);

        return ticks * 1_000_000 / frequency;
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

        // PP397: everything else is A control-style channel, and there are now three of them. The
        // rule is asked of the channel as well as the type, because a ctrl message type and a
        // protobuf payload type are different numbering schemes and this used to consult one list
        // for both - so a BIG carrying the session id was recorded in the clear.
        // PP423: three answers now, not two. The BANG is recorded with three of its nine fields
        // zeroed, because a whole-payload marker hid the console's verdict on the handshake along
        // with the two optional key fields that share the message.
        //
        // A PAYLOAD THAT CANNOT BE WALKED FALLS BACK TO THE MARKER. Blanking nothing and recording
        // it would publish exactly the bytes the rule exists to hide, so the refusal is the marker
        // rather than the payload - PP326's principle, applied to a parse failure.
        string body = MessageSecrets.DisclosureFor(message.Channel, message.Type) switch
        {
            PayloadDisclosure.Whole => Dashed(message.Payload),
            PayloadDisclosure.FieldsBlanked => Blanked(message.Payload),
            _ => MessageSecrets.Marker,
        };

        return $"{message.Type:x4} {body}";
    }

    /// <summary>
    /// PP423: a BANG with its three secret fields zeroed, or the marker where it will not parse.
    /// </summary>
    private static string Blanked(byte[] payload)
        => ProtobufRedaction.Blank(
                payload, MessageSecrets.BangPayloadField, MessageSecrets.BangSecretFields)
            is { } blanked
            ? Dashed(blanked)
            : MessageSecrets.Marker;

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
