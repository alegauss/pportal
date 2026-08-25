using System.Globalization;
using System.Text;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One thing that happened, in the order it happened.</summary>
/// <param name="AtMicroseconds">When, relative to the first entry. Zero for the first.</param>
/// <param name="Direction">Which way it went.</param>
/// <param name="Channel">Which conversation it belongs to - the session request, the control channel.</param>
/// <param name="Payload">What was said, already redacted.</param>
public readonly record struct ExchangeEntry(
    long AtMicroseconds, ExchangeDirection Direction, string Channel, string Payload);

/// <summary>Which way a recorded entry went.</summary>
public enum ExchangeDirection
{
    /// <summary>Client to console.</summary>
    Sent,

    /// <summary>Console to client.</summary>
    Received,
}

/// <summary>
/// PP297: the recording a state machine can be ported against, and the format it is kept in.
///
/// Eleven modules of this port were judged by running the managed side and the C side over the same
/// buffers and diffing. A session cannot be: it is a state machine over a socket, there is no
/// second run, and the C cannot be driven without a console either. So the oracle has to be a
/// recording - what was sent, what came back, and in what order - replayed against both.
///
/// This is the format and the recorder. Making an actual recording needs a console reaching the
/// stream, which is the one step nothing here can do; everything around it is written and checked
/// so that step is a flag rather than a project.
///
/// Redacted on the way IN, not on the way out
/// -------------------------------------------
/// A real exchange carries the account id, the registration key and the nonce. <see cref="Add"/>
/// redacts before the entry is stored, so an unredacted payload never exists in memory long enough
/// to be written by accident - a recording is a file somebody attaches to an issue, and "we redact
/// it when we save" is how the other kind of story starts.
///
/// TWO SANITISERS AND THE ORDER MATTERS (PP325). PP88's runs over a line of text and is held
/// character-for-character against gui/src/sessionlog.cpp. It is not enough on its own here: down
/// the log path RP-Registkey and RP-Nonce were covered by PP320 redacting a hexdump row WHOLE, and
/// a payload arriving structured has no such cover. A base64 nonce matches none of those ten rules.
/// So <see cref="SessionHeaderSanitizer"/> goes FIRST and takes the value under a named header,
/// then PP88's runs over what is left.
///
/// The format is one line per entry
/// --------------------------------
/// Tab-separated, because a recording is read by a person as often as by a test, and a diff of two
/// recordings should be legible. The payload is the last field so a tab inside it cannot shift the
/// others, and newlines in it are escaped rather than quoted.
/// </summary>
public sealed class ExchangeRecording
{
    private readonly List<ExchangeEntry> entries = [];
    private long? originMicroseconds;

    /// <summary>What has been recorded, oldest first.</summary>
    public IReadOnlyList<ExchangeEntry> Entries => entries;

    /// <summary>The format's version, so a replay can refuse one it does not understand.</summary>
    public const string FormatVersion = "chiaki-exchange-1";

    /// <summary>
    /// Records one entry, redacting it first.
    /// </summary>
    /// <param name="atMicroseconds">
    /// A monotonic reading. The first one becomes the origin and every entry is stored relative to
    /// it, so a recording carries no wall-clock time and two of them can be compared directly.
    /// </param>
    public void Add(long atMicroseconds, ExchangeDirection direction, string channel, string payload)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        originMicroseconds ??= atMicroseconds;

        // Clamped at zero rather than allowed negative: a monotonic clock that went backwards is a
        // machine problem, and a recording with a negative offset is one no replay can order.
        long offset = Math.Max(0, atMicroseconds - originMicroseconds.Value);

        // PP325: by field, then by line. See the note on the class for why it is both and this way
        // round - the header rule needs the value still sitting under its name, and PP88's rules can
        // rewrite a line into something no header rule would recognise.
        string redacted = SessionLogSanitizer.Sanitize(SessionHeaderSanitizer.Sanitize(payload));

        entries.Add(new ExchangeEntry(offset, direction, channel, redacted));
    }

    /// <summary>The recording as text, one entry per line.</summary>
    public string Write()
    {
        var text = new StringBuilder();
        text.Append(FormatVersion).Append('\n');

        foreach (ExchangeEntry entry in entries)
        {
            text.Append(entry.AtMicroseconds.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(entry.Direction == ExchangeDirection.Sent ? "->" : "<-").Append('\t')
                .Append(Escape(entry.Channel)).Append('\t')
                .Append(Escape(entry.Payload)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads one back, or null where the text is not a recording this understands.</summary>
    public static ExchangeRecording? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != FormatVersion)
            return null;

        var recording = new ExchangeRecording();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;

            // Three splits, so a tab inside the payload stays in the payload.
            string[] fields = lines[i].TrimEnd('\r').Split('\t', 4);
            if (fields.Length != 4)
                return null;

            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long at))
                return null;

            ExchangeDirection direction = fields[1] switch
            {
                "->" => ExchangeDirection.Sent,
                "<-" => ExchangeDirection.Received,
                _ => (ExchangeDirection)(-1),
            };

            if (direction is not (ExchangeDirection.Sent or ExchangeDirection.Received))
                return null;

            // NOT re-sanitised. What was written was redacted when it was added, and running the
            // patterns again over a payload full of <redacted> would be redacting the redaction.
            recording.entries.Add(new ExchangeEntry(at, direction, Unescape(fields[2]), Unescape(fields[3])));
            recording.originMicroseconds ??= 0;
        }

        return recording;
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                text.Append(value[i]);
                continue;
            }

            i++;
            text.Append(value[i] switch
            {
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                '\\' => '\\',

                // An escape this does not know keeps both characters, so an unescape can never
                // lose data it did not understand.
                _ => '\\',
            });

            if (value[i] is not ('t' or 'r' or 'n' or '\\'))
                text.Append(value[i]);
        }

        return text.ToString();
    }
}
