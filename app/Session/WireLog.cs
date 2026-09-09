using System.Globalization;
using System.IO;
using System.Text;

namespace ChiakiNg.Session;

/// <summary>
/// PP834: both directions of the relay's wire, written beside the replay corpus and not into it.
///
/// BOTH RELAY CALL SITES RECORDED ARRIVALS ONLY, and each said why in the same words: the capture
/// is a REPLAY CORPUS, so offering the sends as well would produce a file the replay reads as
/// twice the traffic. That reason is sound, and the consequence was that this tree could not read
/// its own sends.
///
/// PP813 IS WHAT THAT COST. A senkusha command left on the wrong takion channel for six tasks. The
/// capture held every datagram the console sent and not one the port did, so the investigation
/// compared the port's ping against bytes built through the C's formatter - a model of the send
/// rather than the send. The model agreed, because the builder was right; the wrong argument was a
/// layer below it and nothing recorded that layer.
///
/// SO IT IS A SECOND ARTEFACT AND NOT A COLUMN. A replay needs arrivals alone because it stands in
/// for a console; a diagnosis needs both because half a conversation explains nothing. Adding a
/// direction column to the capture format would break every reader that already agrees about it,
/// which is the one thing §PP834 said this must not do. Its own path, its own flag, its own
/// format - and the corpus is left exactly as it was.
///
/// TEXT, BECAUSE IT IS READ BY A PERSON LOOKING FOR A WRONG BYTE. The replay corpus is a machine's
/// artefact and is shaped for one; this is opened when somebody already suspects a send, and hex
/// on a line that names its leg and direction is what answers that.
/// </summary>
public sealed class WireLog : IDisposable
{
    private readonly StreamWriter writer;
    private readonly Func<long> clock;
    private readonly object gate = new();

    private bool disposed;

    /// <param name="path">Where to write. Its directory must exist, as every capture's does.</param>
    /// <param name="clock">
    /// Monotonic microseconds, which is the capture's own clock so two artefacts of one run can be
    /// read against each other.
    /// </param>
    public WireLog(string path, Func<long> clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(clock);

        this.clock = clock;

        writer = new StreamWriter(path, append: false, Encoding.ASCII) { AutoFlush = false };

        // A header, because this file is read by a person and the columns are not guessable.
        writer.WriteLine("# microseconds leg direction bytes hex");
    }

    /// <summary>How many datagrams this has written, in both directions.</summary>
    public long Rows { get; private set; }

    /// <summary>How many of those the port SENT, which is the half nothing recorded before.</summary>
    public long Sent { get; private set; }

    /// <summary>
    /// One datagram, whole.
    /// </summary>
    /// <param name="datagram">The bytes, as they crossed.</param>
    /// <param name="fromConsole">True where the console sent it; false where this machine did.</param>
    /// <param name="leg">Which of the two UDP conversations it crossed.</param>
    public void Offer(byte[] datagram, bool fromConsole, RelayLeg leg)
    {
        ArgumentNullException.ThrowIfNull(datagram);

        // Locked because both legs forward on their own threads, and a row torn in half is a row
        // nobody can read. The write is buffered, so the lock is held for a memcpy and not for IO -
        // which matters: SessionRelay's own note is that a recorder blocking here delays the stream
        // it is recording.
        lock (gate)
        {
            if (disposed)
                return;

            writer.Write(clock().ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(leg == RelayLeg.Senkusha ? "senkusha" : "stream");
            writer.Write(' ');
            writer.Write(fromConsole ? "in " : "out");
            writer.Write(' ');
            writer.Write(datagram.Length.ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.WriteLine(Convert.ToHexString(datagram));

            Rows++;

            if (!fromConsole)
                Sent++;
        }
    }

    /// <summary>Flushes what is buffered and closes the file.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            writer.Flush();
            writer.Dispose();
        }
    }
}
