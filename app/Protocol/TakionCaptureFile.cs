using System.Globalization;
using System.Text;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP512, under PP27: the capture as a file, and the writer that owns the tap while it fills.
///
/// PP510 settled what a timing run keeps and PP511 made the C emit it. Neither got it out of the
/// process: the capture is an object, and the session PP27 waits for would fill one and end.
///
/// THE FORMAT FOLLOWS PP297'S, because a second convention costs a reader. A version line, then one
/// row per datagram - arrival, length, base type, head as hex - tab separated, the way
/// <see cref="ExchangeRecording"/> writes its own. Versioned for the same reason: a file read
/// months later has to say what it is.
///
/// WHAT CROSSES IS NOT WHAT A CORPUS CROSSES, and that is stated rather than inherited. PP326
/// redacts because its channels carry what somebody typed. A head carries no payload at all - PP510
/// truncated it at the MAC gate's furthest read - but it does carry that packet's GMAC and key
/// position. Neither is the key, both are per session, and the payload never leaves. So a capture
/// is one session's measurement artefact and not a corpus to commit, which is why nothing here
/// writes into the tree's vectors directory and why this says so out loud.
///
/// Hex rather than the dash-separated pairs PP326 chose: that shape exists to survive the log
/// sanitiser's ten patterns, and nothing here is sanitised, because nothing here is text somebody
/// wrote.
/// </summary>
public static class TakionCaptureFile
{
    /// <summary>What the first line says.</summary>
    public const string FormatVersion = "chiaki-datagrams-2";

    /// <summary>
    /// PP520: the version before PP515, whose Length column holds the HEAD's length.
    ///
    /// Recognised so a refusal can name it. PP512 versioned this format saying a file read months
    /// later has to say what it is, and PP515 then changed what a column means without moving the
    /// version - so the one file written under it replays without complaint and reports every video
    /// packet as eighteen bytes. A number that is wrong and looks measured is worse than one that
    /// is missing.
    /// </summary>
    public const string HeadLengthVersion = "chiaki-datagrams-1";

    /// <summary>
    /// Whether text is a capture written before PP515, whose lengths cannot be believed.
    ///
    /// Not read with a warning: the datagram's length is not recoverable from a head, so every
    /// figure a replay prints about size would be false.
    /// </summary>
    public static bool IsHeadLengthVersion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Split('\n');
        return lines.Length > 0 && lines[0].Trim() == HeadLengthVersion;
    }

    /// <summary>Writes a capture out.</summary>
    public static string Write(TakionTimingCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var text = new StringBuilder();
        text.Append(FormatVersion).Append('\n');

        foreach (CapturedDatagram datagram in capture.Datagrams)
        {
            text.Append(datagram.ArrivalMicroseconds.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(datagram.Length.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(datagram.BaseType.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(Convert.ToHexString(datagram.Head)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads one back, or null where the text is not a capture this understands.
    /// </summary>
    /// <remarks>
    /// Null and not an exception, for the reason ExchangeRecording gives: the caller is deciding
    /// whether a file on disk is one of these, and a wrong guess is an ordinary answer.
    /// </remarks>
    public static IReadOnlyList<CapturedDatagram>? Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != FormatVersion)
            return null;

        var datagrams = new List<CapturedDatagram>();

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;

            string[] fields = lines[i].TrimEnd('\r').Split('\t');
            if (fields.Length != 4)
                return null;

            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long at)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int length)
                || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int baseType))
            {
                return null;
            }

            byte[] head;
            try
            {
                head = Convert.FromHexString(fields[3]);
            }
            catch (FormatException)
            {
                return null;
            }

            datagrams.Add(new CapturedDatagram(at, length, baseType, head));
        }

        return datagrams;
    }
}

/// <summary>
/// PP512: one object that installs the tap, holds the capture, and writes on the way out.
///
/// The point is that a session cannot end having recorded into something nobody flushed. Install
/// it where a stream starts, dispose it where the stream ends, and the file is there either way -
/// including when the session ended badly, which is the run most worth having.
/// </summary>
public sealed class TakionCaptureWriter : IDisposable
{
    private readonly TakionDatagramTap? tap;
    private readonly string path;
    private bool disposed;

    /// <summary>
    /// Starts capturing into a file.
    /// </summary>
    /// <param name="path">Where the capture goes. Written on dispose, once.</param>
    /// <param name="clock">Monotonic microseconds.</param>
    /// <param name="capture">The capture to fill, or null for one with the default bounds.</param>
    /// <param name="installTap">
    /// PP616: whether the C's tap is what fills this.
    ///
    /// True is the ordinary run and the only one there was. False is a capture through PP613's
    /// relay, where the caller offers the datagrams itself - and installing a tap as well would
    /// record every arrival twice, once whole and once at the tap's eighteen bytes, in one file
    /// that says nothing about which is which.
    /// </param>
    public TakionCaptureWriter(
        string path, Func<long> clock, TakionTimingCapture? capture = null, bool installTap = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(clock);

        this.path = path;
        Capture = capture ?? new TakionTimingCapture();
        tap = installTap ? new TakionDatagramTap(Capture, clock) : null;
    }

    /// <summary>What has been captured so far.</summary>
    public TakionTimingCapture Capture { get; }

    /// <summary>Uninstalls the tap and writes the file. Idempotent.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        // The tap goes first: writing while the receive thread is still offering would race the
        // list this is about to read. PP616: null where a relay is the source, and there the
        // caller's own disposal is what stops the offering - same ordering, its to keep.
        tap?.Dispose();

        File.WriteAllText(path, TakionCaptureFile.Write(Capture));
    }
}
