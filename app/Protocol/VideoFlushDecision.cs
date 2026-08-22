namespace ChiakiNg.Protocol;

/// <summary>What a failed flush asks the receiver to do.</summary>
/// <param name="ReportCorrupt">Send a corrupt-frame message for the range below.</param>
/// <param name="CorruptFrom">First frame index in that range.</param>
/// <param name="CorruptTo">Last one, which is the frame that failed.</param>
/// <param name="RequestIdr">Ask the console for a keyframe.</param>
/// <param name="IdrRequestSent">What the FEC-failure event carries, which is not the same question.</param>
/// <param name="FramesLost">How many frames to charge, reproducing the C's arithmetic exactly.</param>
public readonly record struct FlushFailure(
    bool ReportCorrupt, ushort CorruptFrom, ushort CorruptTo,
    bool RequestIdr, bool IdrRequestSent, int FramesLost);

/// <summary>
/// PP291: what videoreceiver.c does when a frame cannot be completed, as a decision.
///
/// The FEC-failure path is the part that talks to the console - a corrupt-frame message, sometimes
/// an IDR request, and an event - so it cannot be ported as a function that acts. This is the
/// deciding half: given the flush result and the receiver's state, what should be sent and what
/// should be counted. The sending itself is the caller's, through the delegate seam §PP291 names.
///
/// Only FEC_FAILED is charged
/// --------------------------
/// The C indents as though the accounting applies to any failed flush. It does not - the braces put
/// it inside `if(flush_result == FEC_FAILED)`, so a plain FAILED logs and returns without touching
/// frames_lost or frame_index_prev. Reproduced, because a port that charged both would report loss
/// on every frame flushed twice.
/// </summary>
public static class VideoFlushDecision
{
    /// <summary>
    /// The decision for a flush that did not produce a frame.
    /// </summary>
    /// <param name="result">What the frame processor answered.</param>
    /// <param name="frameIndexCur">The frame that failed.</param>
    /// <param name="frameIndexPrevComplete">The last frame decoded whole.</param>
    /// <param name="idrOnFecFailure">connect_info.enable_idr_on_fec_failure.</param>
    /// <param name="waitingForIdr">Whether a keyframe has already been asked for and not arrived.</param>
    public static FlushFailure Failed(
        FrameFlushResult result, int frameIndexCur, int frameIndexPrevComplete,
        bool idrOnFecFailure, bool waitingForIdr)
    {
        if (result != FrameFlushResult.FecFailed)
            return default;

        var nextExpected = (ushort)(frameIndexPrevComplete + 1);

        // Already waiting counts as sent, which is what the event carries. The distinction matters
        // to a reader of the event stream: two failures in a row produce two events, and only the
        // first of them was a request.
        bool requestIdr = idrOnFecFailure && !waitingForIdr;
        bool idrRequestSent = idrOnFecFailure && waitingForIdr;

        return new FlushFailure(
            ReportCorrupt: true,
            CorruptFrom: nextExpected,
            CorruptTo: (ushort)frameIndexCur,
            RequestIdr: requestIdr,
            IdrRequestSent: idrRequestSent,
            FramesLost: FramesLost(frameIndexCur, frameIndexPrevComplete));
    }

    /// <summary>
    /// How many frames the failure charges, reduced against the sequence wrap.
    ///
    /// PP292, and the one thing in this file the port does NOT reproduce. What upstream ships is
    /// <c>int32_t lost = frame_index_cur - next_frame_expected + 1;</c> - next_frame_expected is a
    /// ChiakiSeqNum16 that has already turned over, frame_index_cur is an int32 holding the same
    /// range and not reduced against it, and C's promotion makes that a plain subtraction of a
    /// large number from a small one. Correct until the counter wraps and then not: at
    /// prev_complete 65530 and cur 2 it answers -65528 where 8 was meant, into frames_lost and
    /// frames_lost_total both, about every eighteen minutes of 60fps streaming.
    ///
    /// PP291 reproduced it, on this port's standing rule for a defect it did not introduce: a
    /// managed side quietly counting differently would disagree with the C for reasons no
    /// comparison between them could explain. PP292 closes that the other way instead - the C was
    /// fixed in the same commit, so the two still agree and what they agree on is right.
    /// <see cref="VideoReceiverSource.TheLostCountIsStillReducedAgainstTheWrap"/> holds it there,
    /// and turns red if lib/src/videoreceiver.c ever goes back.
    /// </summary>
    public static int FramesLost(int frameIndexCur, int frameIndexPrevComplete)
        => (ushort)(frameIndexCur - (ushort)(frameIndexPrevComplete + 1) + 1);

    /// <summary>
    /// Whether a decoded slice clears the IDR wait, and whether the frame should be skipped.
    ///
    /// While waiting, a P-frame is dropped without reaching the decoder - it is a difference against
    /// a frame the decoder never got. An I-frame is what the wait was for, so it clears it and is
    /// delivered.
    /// </summary>
    /// <returns>true where the frame should be skipped rather than decoded.</returns>
    public static bool SkipWhileWaitingForIdr(bool waitingForIdr, bool isIntraSlice, out bool clearsWait)
    {
        clearsWait = waitingForIdr && isIntraSlice;
        return waitingForIdr && !isIntraSlice;
    }
}
