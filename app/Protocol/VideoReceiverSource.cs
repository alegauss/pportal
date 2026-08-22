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
}
