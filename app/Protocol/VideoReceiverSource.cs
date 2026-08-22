using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP291: the four comparisons <see cref="VideoFrameSequencer"/> was read out of, where the C makes
/// them.
///
/// The ordering decision has no bytes in it, so there is nothing to diff against the native
/// receiver the way PP287 and PP289 could - the decision is not observable through the shim, only
/// its consequences several layers down. What is left is the port's other assertion: read the C and
/// say the conditions are still the ones the managed table reproduces.
///
/// That is weaker than a differential test and it is not weak. A table of cases derived from four
/// comparisons goes on passing forever if one of those comparisons changes; it would simply be
/// describing a receiver that no longer exists, which is the failure this whole family of readers
/// was built for.
/// </summary>
public static class VideoReceiverSource
{
    /// <summary>Where the decision lives, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\videoreceiver.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether the stale test still checks that a frame is in progress BEFORE comparing against it.
    ///
    /// Without the guard, frame_index_cur is -1 on the first packet of a session, cast to a
    /// sequence number that is 65535, and every early frame reads as older than it.
    /// </summary>
    public static bool OldFrameIsStillGuardedOnCurrent(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("video_receiver->frame_index_cur >= 0", StringComparison.Ordinal)
            && core.Contains(
                "chiaki_seq_num_16_lt(frame_index, (ChiakiSeqNum16)video_receiver->frame_index_cur)",
                StringComparison.Ordinal);
    }

    /// <summary>And that a new frame is still one greater than the current, as a sequence number.</summary>
    public static bool ANewFrameIsStillGreaterThanCurrent(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("video_receiver->frame_index_cur < 0 ||", StringComparison.Ordinal)
            && core.Contains(
                "chiaki_seq_num_16_gt(frame_index, (ChiakiSeqNum16)video_receiver->frame_index_cur)",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a gap is still measured from the last COMPLETE frame rather than the last seen.
    ///
    /// The two differ whenever a frame was started and abandoned, which is exactly when a gap is
    /// being reported - so a port that used the wrong one would be wrong only while it mattered.
    /// </summary>
    public static bool TheGapIsStillFromPrevComplete(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "next_frame_expected = (ChiakiSeqNum16)(video_receiver->frame_index_prev_complete + 1)",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether frame 1 is still excepted from the gap report when nothing has been seen.
    ///
    /// One condition, and it fires on the first frame of every session: without it the client opens
    /// by asking the console to resend a frame 0 that was never sent.
    /// </summary>
    public static bool FrameOneIsStillExcepted(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "!(frame_index == 1 && video_receiver->frame_index_cur < 0)", StringComparison.Ordinal);
    }

    /// <summary>Whether the reference ring still shifts down once slot 0 is occupied.</summary>
    public static bool TheRingStillShiftsFromSlotZero(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("video_receiver->reference_frames[0] != -1", StringComparison.Ordinal)
            && core.Contains(
                "memmove(&video_receiver->reference_frames[1], &video_receiver->reference_frames[0]",
                StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether it still backfills from the END before that.
    ///
    /// The loop runs from 15 downward, so the first frame of a session lands in the LAST slot. A
    /// port that filled forwards is indistinguishable for sixteen frames and then holds a different
    /// set, because the shift only begins once slot 0 is taken.
    /// </summary>
    public static bool TheRingStillBackfillsFromTheEnd(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("for(int i=15; i>=0; i--)", StringComparison.Ordinal)
            && core.Contains("video_receiver->reference_frames[i] == -1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a substitute is still searched for FORWARDS from the asked-for index.
    ///
    /// Forwards means further back in time, which is the only direction that can help: a nearer
    /// reference has not been decoded yet.
    /// </summary>
    public static bool TheSubstituteIsStillSearchedForwards(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("for(unsigned i=slice.reference_frame+1; i<16; i++)", StringComparison.Ordinal);
    }

    /// <summary>And whether a slice naming no reference is still excepted from that search.</summary>
    public static bool NoReferenceIsStillSkipped(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("slice.reference_frame != 0xff", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP292: whether the frames-lost count is still reduced against the sequence wrap.
    ///
    /// This one asks the opposite question of every other reader in this file, and it is the only
    /// place the port and the C differ from what upstream ships. The rest hold "the C still does
    /// what this was translated from"; PP292 CHANGED the C, in the same commit as the managed side,
    /// so what has to hold is that the cast is still there. Without it the subtraction promotes and
    /// answers -65528 across the turnover, and the two implementations would disagree again - this
    /// time with the managed one right, which is no better for a comparison.
    ///
    /// The likely way it goes false is a merge from upstream, where the old line comes back
    /// unremarked because nothing about it looks like a conflict.
    /// </summary>
    public static bool TheLostCountIsStillReducedAgainstTheWrap(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains(
            "int32_t lost = (ChiakiSeqNum16)(video_receiver->frame_index_cur - next_frame_expected + 1)",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And whether the event's idr_request_sent is still seeded from the wait rather than the send.
    ///
    /// The two are different questions with almost the same name: on a second failure the flag is
    /// true because a request is outstanding, not because one just went out.
    /// </summary>
    public static bool TheIdrSentFlagIsStillSeededFromTheWait(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("bool idr_request_sent = waiting_for_idr;", StringComparison.Ordinal);
    }
}
