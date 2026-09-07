namespace ChiakiNg.Session;

/// <summary>
/// PP5: the audio ring from streamsession.cpp, with the QMutex and SDL taken off it.
///
/// ONE ring, where the Qt client has two. QueueAudioOutData and QueueMicData are the same fifty
/// lines twice - the same three overflow branches, the same drop-oldest policy, the same depth of
/// eight frames - differing only in that the output drain stops at a target queue size and the
/// microphone drain does not. That is <see cref="Read(int)"/> against <see cref="Read()"/> here,
/// and nothing else. Two copies of a ring buffer is two places for a fix to land in one of.
///
/// It is the piece of the audio path that decides whether a slow sink costs latency or costs a
/// dropout. Getting it wrong is not a crash: it is audio that drifts a second behind the picture
/// over an evening, or that clicks every few minutes, and neither points at a ring buffer.
///
/// The policy is drop the OLDEST, always. A stream's audio is only worth playing if it is current,
/// so when the producer outruns the sink the ring throws away what the listener has not heard yet
/// rather than refusing what just arrived. Two of the three branches exist to say that precisely:
/// a write larger than the whole ring keeps its own TAIL and discards its head, and a write that
/// merely does not fit advances the read cursor over exactly as much as it needs.
///
/// The multipliers around it are the latency policy and live at the call sites in the Qt client:
/// both rings are their own frame size times eight, the output drain fills the sink to times two,
/// and a sink holding more than times three is cleared outright. They are named below so the port
/// cannot pick different ones by accident.
///
/// PP815: AND THE QMutex IS BACK, because the port grew the second thread the Qt client had.
/// Removing it was right when this shipped - the ring had one caller and no thread of its own. PP797
/// joined the decoder to it, so <see cref="Write"/> now runs on the takion receive thread while
/// <see cref="Read(int)"/> and <see cref="Fill"/> run on the drain thread, which is exactly the pair
/// the QMutex was there for.
///
/// WHAT WENT WRONG WITHOUT IT WAS CONTENT AND NOT COUNTING, which is why every audio counter read
/// healthy while a listener heard a click every one to two seconds. <see cref="Fill"/> moves by a
/// read-modify-write on both sides and the overflow branch below advances readPos - the READER's
/// cursor - before copying, so a drain could take a span the writer was in the middle of. Nothing is
/// dropped and nothing overflows; the bytes are counted written and counted drained, and only what
/// they hold is wrong. PP716 is the precedent for keeping a lock the C does without.
/// </summary>
public sealed class AudioRing
{
    private readonly object gate = new();
    private readonly byte[] buffer;
    private int readPos;
    private int writePos;
    private int fill;
    private bool overflowReported;

    public AudioRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        buffer = new byte[capacity];
    }

    /// <summary>
    /// Eight frames deep, which is both rings: audio_buffer_size * 8 for the output and
    /// mic_buf.size_bytes * 8 for the microphone.
    /// </summary>
    public static int CapacityFor(int frameSize) => frameSize * 8;

    /// <summary>The output drain stops once the sink holds two buffers. The mic drain has no target.</summary>
    public static int DrainTargetFor(int audioBufferSize) => audioBufferSize * 2;

    /// <summary>An output sink holding more than three buffers is behind, and is cleared rather than drained.</summary>
    public static int ClearThresholdFor(int audioBufferSize) => audioBufferSize * 3;

    public int Capacity => buffer.Length;

    /// <summary>
    /// How many bytes are waiting.
    /// </summary>
    /// <remarks>
    /// PP815: read under the gate, because the drain tests this and then acts on it. An unguarded
    /// read is a torn int in principle and a stale one in practice, and the drain's loop condition
    /// is the one place a stale answer turns into a read past what is really there.
    /// </remarks>
    public int Fill
    {
        get { lock (gate) return fill; }
    }

    /// <summary>
    /// Whether an overflow has been reported since the ring last ran dry. The Qt client logs on
    /// the first drop and then stays quiet until the ring empties, so a sink that is permanently
    /// slow produces one line rather than one per frame.
    /// </summary>
    public bool OverflowReported
    {
        get { lock (gate) return overflowReported; }
    }

    /// <summary>
    /// Writes a frame, dropping the oldest bytes if it does not fit.
    ///
    /// A frame at least as large as the whole ring is not an error and is not refused: the ring is
    /// reset and the frame's LAST <see cref="Capacity"/> bytes are kept. Keeping the head instead
    /// would play the oldest slice of a frame that is already too late.
    /// </summary>
    /// <returns>true when something already queued was dropped to make room.</returns>
    public bool Write(ReadOnlySpan<byte> data)
    {
        if (Capacity == 0 || data.Length == 0)
            return false;

        // PP815: the eviction below moves readPos and the copy below that writes into the ring, so
        // the whole of it has to be one transaction against a drain reading from the same cursor.
        lock (gate)
        {
            bool dropped = false;

            if (data.Length >= Capacity)
            {
                data = data[^Capacity..];
                readPos = 0;
                writePos = 0;
                fill = 0;
                dropped = true;
            }
            else if (data.Length > Capacity - fill)
            {
                int toDrop = data.Length - (Capacity - fill);
                readPos = (readPos + toDrop) % Capacity;
                fill -= toDrop;
                dropped = true;
                overflowReported = true;
            }

            int firstCopy = Math.Min(data.Length, Capacity - writePos);
            data[..firstCopy].CopyTo(buffer.AsSpan(writePos));
            if (data.Length > firstCopy)
                data[firstCopy..].CopyTo(buffer.AsSpan(0));

            writePos = (writePos + data.Length) % Capacity;
            fill += data.Length;
            return dropped;
        }
    }

    /// <summary>
    /// The microphone drain: one contiguous chunk, as much as there is. The capture path has no
    /// target queue size to stop at - what it has captured goes to the console.
    /// </summary>
    public byte[] Read() => Read(Capacity);

    /// <summary>
    /// One contiguous chunk, at most <paramref name="maxBytes"/> and never across the seam.
    ///
    /// The seam bound is not an optimisation, it is what the Qt client does - the chunk is a slice
    /// of the ring's own storage - so a drain that wants more than the tail holds gets it in two
    /// turns of the loop. A port that returned a stitched buffer instead would read the same bytes
    /// and take a different number of iterations to do it, which is the sort of difference that
    /// only shows up as a timing change.
    /// </summary>
    public byte[] Read(int maxBytes)
    {
        // PP815: the copy out is inside the gate, not just the cursor arithmetic. Taking the span
        // outside it is the tear - a writer evicting moves readPos and then overwrites the bytes
        // this is in the middle of copying, and the chunk comes back holding two frames.
        lock (gate)
        {
            if (fill == 0)
            {
                // Running dry re-arms the log, so the next slow patch is reported again.
                overflowReported = false;
                return [];
            }

            int chunk = Math.Min(fill, Math.Min(Math.Max(maxBytes, 0), Capacity - readPos));
            if (chunk <= 0)
                return [];

            byte[] outBuf = buffer.AsSpan(readPos, chunk).ToArray();
            readPos = (readPos + chunk) % Capacity;
            fill -= chunk;
            return outBuf;
        }
    }

    /// <summary>What the Qt client does when the sink is more than three buffers behind.</summary>
    public void Reset()
    {
        lock (gate)
        {
            readPos = 0;
            writePos = 0;
            fill = 0;
            overflowReported = false;
        }
    }
}
