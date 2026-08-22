namespace ChiakiNg.Protocol;

/// <summary>What arriving packet means for the frame being assembled.</summary>
/// <param name="Stale">The packet belongs to a frame already past. Nothing else in this record applies.</param>
/// <param name="StartsFrame">This packet opens a new frame, so the previous one is finished with.</param>
/// <param name="FlushPrevious">The previous frame was never flushed and must be, before this one starts.</param>
/// <param name="CorruptFrom">First frame index to report as missing, where <paramref name="ReportCorrupt"/>.</param>
/// <param name="CorruptTo">Last one, inclusive.</param>
/// <param name="ReportCorrupt">Whether a gap was detected and the console should be told.</param>
public readonly record struct FrameArrival(
    bool Stale, bool StartsFrame, bool FlushPrevious, ushort CorruptFrom, ushort CorruptTo, bool ReportCorrupt);

/// <summary>
/// PP291: the frame ordering decision out of videoreceiver.c, which is the part with no buffers in it.
///
/// chiaki_video_receiver_av_packet does three things: it decides what an arriving packet means for
/// the frame in progress, it moves bytes, and it talks to the console. PP289 ported the bytes. This
/// is the first - four sequence numbers and the comparisons between them - and it is the half where
/// being wrong is invisible until a real network drops a frame.
///
/// Sequence numbers, not integers
/// ------------------------------
/// Frame indices are 16 bits and they wrap. Every comparison here goes through <see cref="SeqNum"/>
/// rather than through &lt; and &gt;, which is the difference between "frame 1 follows frame 65535"
/// and "the receiver threw away everything for eighteen minutes". The C is careful about this and a
/// port that used int would pass every test written on small numbers.
///
/// The exception for frame 1
/// -------------------------
/// A gap is reported whenever the arriving frame is beyond the next one expected - EXCEPT when it
/// is frame 1 and no frame has been seen at all. That is the first frame of a session arriving
/// after frame 0 was never sent, and reporting it would have the client ask the console to resend
/// a frame that never existed. One condition in the C, easy to drop, and it only shows on the very
/// first frame of every session.
/// </summary>
public static class VideoFrameSequencer
{
    /// <summary>No frame has been seen. The C uses a negative index for this.</summary>
    public const int NoFrame = -1;

    /// <summary>
    /// What to do with a packet, given where the receiver is.
    /// </summary>
    /// <param name="frameIndex">The arriving packet's frame index.</param>
    /// <param name="frameIndexCur">The frame being filled, or <see cref="NoFrame"/>.</param>
    /// <param name="frameIndexPrev">The last frame at least partially decoded.</param>
    /// <param name="frameIndexPrevComplete">The last frame decoded whole, which is what a gap is measured from.</param>
    public static FrameArrival Arrive(
        ushort frameIndex, int frameIndexCur, int frameIndexPrev, int frameIndexPrevComplete)
    {
        // Old frame. Guarded on frameIndexCur being valid first: before any frame has arrived there
        // is nothing for the packet to be older than, and comparing against -1 as a sequence number
        // would make the first packet of a session look stale.
        if (frameIndexCur >= 0 && SeqNum.Lt(frameIndex, (ushort)frameIndexCur))
            return new FrameArrival(Stale: true, false, false, 0, 0, false);

        bool startsFrame = frameIndexCur < 0 || SeqNum.Gt(frameIndex, (ushort)frameIndexCur);
        if (!startsFrame)
            return new FrameArrival(false, StartsFrame: false, false, 0, 0, false);

        // The previous frame is flushed only if it was started and never completed. Flushing one
        // that was already flushed is what PP289 measured as destructive - the second flush returns
        // a frame of the right length made of the wrong bytes.
        bool flushPrevious = frameIndexCur >= 0 && frameIndexPrev != frameIndexCur;

        var nextExpected = (ushort)(frameIndexPrevComplete + 1);
        bool gap = SeqNum.Gt(frameIndex, nextExpected) && !(frameIndex == 1 && frameIndexCur < 0);

        return new FrameArrival(
            Stale: false,
            StartsFrame: true,
            FlushPrevious: flushPrevious,
            CorruptFrom: nextExpected,
            CorruptTo: (ushort)(frameIndex - 1),
            ReportCorrupt: gap);
    }
}
