namespace ChiakiNg.Session;

/// <summary>
/// PP5: the audio output ring from streamsession.cpp, with the QMutex and SDL taken off it.
///
/// What is left is a drop-oldest ring with three distinct overflow behaviours, and it is the piece
/// of the audio path that decides whether a slow sink costs latency or costs a dropout. Getting it
/// wrong is not a crash: it is audio that drifts a second behind the picture over an evening, or
/// that clicks every few minutes, and neither points at a ring buffer.
///
/// The policy is drop the OLDEST, always. A stream's audio is only worth playing if it is current,
/// so when the producer outruns the sink the ring throws away what the listener has not heard yet
/// rather than refusing what just arrived. Two of the three branches exist to say that precisely:
/// a write larger than the whole ring keeps its own TAIL and discards its head, and a write that
/// merely does not fit advances the read cursor over exactly as much as it needs.
///
/// The multipliers around it are the latency policy and live at the call sites in the Qt client:
/// the ring is <c>audio_buffer_size * 8</c>, the drain fills the sink to <c>* 2</c>, and a sink
/// holding more than <c>* 3</c> is cleared outright. They are named in <see cref="CapacityFor"/>
/// and the two properties beside it so the port cannot pick different ones by accident.
/// </summary>
public sealed class AudioOutRing
{
    private readonly byte[] buffer;
    private int readPos;
    private int writePos;

    public AudioOutRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        buffer = new byte[capacity];
    }

    /// <summary>The ring is eight audio buffers deep.</summary>
    public static int CapacityFor(int audioBufferSize) => audioBufferSize * 8;

    /// <summary>The drain stops once the sink holds two buffers.</summary>
    public static int DrainTargetFor(int audioBufferSize) => audioBufferSize * 2;

    /// <summary>A sink holding more than three buffers is behind, and is cleared rather than drained.</summary>
    public static int ClearThresholdFor(int audioBufferSize) => audioBufferSize * 3;

    public int Capacity => buffer.Length;

    /// <summary>How many bytes are waiting.</summary>
    public int Fill { get; private set; }

    /// <summary>
    /// Whether an overflow has been reported since the ring last ran dry. The Qt client logs on
    /// the first drop and then stays quiet until the ring empties, so a sink that is permanently
    /// slow produces one line rather than one per frame.
    /// </summary>
    public bool OverflowReported { get; private set; }

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

        bool dropped = false;

        if (data.Length >= Capacity)
        {
            data = data[^Capacity..];
            readPos = 0;
            writePos = 0;
            Fill = 0;
            dropped = true;
        }
        else if (data.Length > Capacity - Fill)
        {
            int toDrop = data.Length - (Capacity - Fill);
            readPos = (readPos + toDrop) % Capacity;
            Fill -= toDrop;
            dropped = true;
            OverflowReported = true;
        }

        int firstCopy = Math.Min(data.Length, Capacity - writePos);
        data[..firstCopy].CopyTo(buffer.AsSpan(writePos));
        if (data.Length > firstCopy)
            data[firstCopy..].CopyTo(buffer.AsSpan(0));

        writePos = (writePos + data.Length) % Capacity;
        Fill += data.Length;
        return dropped;
    }

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
        if (Fill == 0)
        {
            // Running dry re-arms the log, so the next slow patch is reported again.
            OverflowReported = false;
            return [];
        }

        int chunk = Math.Min(Fill, Math.Min(Math.Max(maxBytes, 0), Capacity - readPos));
        if (chunk <= 0)
            return [];

        byte[] outBuf = buffer.AsSpan(readPos, chunk).ToArray();
        readPos = (readPos + chunk) % Capacity;
        Fill -= chunk;
        return outBuf;
    }

    /// <summary>What the Qt client does when the sink is more than three buffers behind.</summary>
    public void Reset()
    {
        readPos = 0;
        writePos = 0;
        Fill = 0;
        OverflowReported = false;
    }
}
