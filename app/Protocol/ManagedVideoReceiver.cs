using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>What the receiver has to send, which is the half that is not a decision.</summary>
public interface IVideoReceiverOutbound
{
    /// <summary>Tell the console frames <paramref name="from"/> to <paramref name="to"/> were lost.</summary>
    void SendCorruptFrame(ushort from, ushort to);

    /// <summary>Ask for a keyframe. Returns whether the request went out.</summary>
    bool SendIdrRequest();

    /// <summary>Report that FEC could not rebuild a frame.</summary>
    void FecFailure(int frameIndex, bool idrRequestSent);
}

/// <summary>
/// PP291: videoreceiver.c, assembled out of the decisions the last three iterations ported.
///
/// The decisions have no session in them and this does - a corrupt-frame message, an IDR request
/// and an event all go somewhere. That somewhere is <see cref="IVideoReceiverOutbound"/> rather
/// than a session pointer, because the six things videoreceiver.c actually reads off the session
/// are settings and the four things it does are messages. Measured before it was designed: §PP291
/// has the count.
///
/// What it owns is the frame in progress, the sixteen reference frames, the profile in use and the
/// IDR wait. Everything else is a call into a decision that was agreed separately.
/// </summary>
public sealed class ManagedVideoReceiver
{
    private readonly IVideoReceiverOutbound outbound;
    private readonly VideoSampleHandler handler;
    private readonly bool idrOnFecFailure;

    /// <summary>
    /// The slice parser, or null where the caller gave no codec.
    ///
    /// Null is a real case rather than a degenerate one: without it every frame is delivered and no
    /// reference is ever substituted, which is the SAFE wrong answer. The opposite - guessing a
    /// slice type - would silently drop P-frames.
    /// </summary>
    private readonly Bitstream? bitstream;

    private readonly FrameAssembler assembler = new();
    private readonly ReferenceFrames references = new();
    private readonly List<byte[]> profiles = [];

    private int profileCur = -1;
    private int frameIndexCur = VideoFrameSequencer.NoFrame;
    private int frameIndexPrev = VideoFrameSequencer.NoFrame;
    private int frameIndexPrevComplete = VideoFrameSequencer.NoFrame;
    private bool waitingForIdr;

    /// <summary>
    /// </summary>
    /// <param name="handler">Where a completed frame goes.</param>
    /// <param name="outbound">Where the messages go.</param>
    /// <param name="idrOnFecFailure">connect_info.enable_idr_on_fec_failure.</param>
    /// <param name="bitstream">
    /// The slice parser. Optional, and its absence is the safe direction - see the field.
    /// </param>
    public ManagedVideoReceiver(
        VideoSampleHandler handler, IVideoReceiverOutbound outbound,
        bool idrOnFecFailure = false, Bitstream? bitstream = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(outbound);

        this.handler = handler;
        this.outbound = outbound;
        this.idrOnFecFailure = idrOnFecFailure;
        this.bitstream = bitstream;
    }

    /// <summary>Frames lost since the last one delivered, which is what the callback is handed.</summary>
    public int FramesLost { get; private set; }

    /// <summary>And the session total, which is never reset.</summary>
    public int FramesLostTotal { get; private set; }

    /// <summary>The profiles a session opens with, in adaptive-stream-index order.</summary>
    public void StreamInfo(params byte[][] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        profiles.Clear();
        profiles.AddRange(headers);
        profileCur = -1;
    }

    /// <summary>One AV packet.</summary>
    public void AvPacket(
        ushort frameIndex, ushort unitIndex, ushort total, ushort fec,
        ReadOnlySpan<byte> data, byte adaptiveStreamIndex = 0)
    {
        FrameArrival arrival = VideoFrameSequencer.Arrive(
            frameIndex, frameIndexCur, frameIndexPrev, frameIndexPrevComplete);

        if (arrival.Stale)
            return;

        // The profile check sits BETWEEN the stale test and the new-frame test in the C, and the
        // order is load-bearing: a stale packet must not switch profiles, and a profile switch has
        // to happen before the frame it belongs to is allocated.
        if (profileCur < 0 || profileCur != adaptiveStreamIndex)
        {
            if (adaptiveStreamIndex >= profiles.Count)
                return;

            profileCur = adaptiveStreamIndex;

            // The header reaches the callback as a frame-shaped thing that is not a frame, which is
            // what a decoder needs before any picture arrives.
            handler(profiles[profileCur], 0, false);
        }

        if (arrival.StartsFrame)
        {
            if (arrival.FlushPrevious)
                Flush();

            if (arrival.ReportCorrupt)
                outbound.SendCorruptFrame(arrival.CorruptFrom, arrival.CorruptTo);

            frameIndexCur = frameIndex;
            assembler.AllocFrame(isVideo: true, unitIndex, total, fec, data);
        }

        assembler.PutUnit(unitIndex, total, data);

        if (frameIndexCur != frameIndexPrev
            && (assembler.FlushPossible || unitIndex == total - 1))
        {
            Flush();
        }
    }

    private void Flush()
    {
        FrameFlushResult result = assembler.Flush(out ReadOnlySpan<byte> frame);

        if (result is FrameFlushResult.Failed or FrameFlushResult.FecFailed)
        {
            FlushFailure failure = VideoFlushDecision.Failed(
                result, frameIndexCur, frameIndexPrevComplete, idrOnFecFailure, waitingForIdr);

            if (failure.ReportCorrupt)
            {
                outbound.SendCorruptFrame(failure.CorruptFrom, failure.CorruptTo);

                bool sent = failure.IdrRequestSent;
                if (failure.RequestIdr && outbound.SendIdrRequest())
                {
                    waitingForIdr = true;
                    sent = true;
                }

                if (idrOnFecFailure)
                    outbound.FecFailure(frameIndexCur, sent);

                FramesLost += failure.FramesLost;
                FramesLostTotal += failure.FramesLost;
                frameIndexPrev = frameIndexCur;
            }

            return;
        }

        // A frame is not necessarily decodable just because it assembled. The slice says whether it
        // is a keyframe - what an IDR wait is waiting for - and which earlier frame a P-frame is a
        // difference against.
        //
        // Copied out of the assembler's buffer because a substitution WRITES to it, and the
        // assembler's is the one the next frame is built in.
        byte[] picture = frame.ToArray();
        if (!Decodable(picture, out bool recovered))
        {
            frameIndexPrev = frameIndexCur;
            return;
        }

        if (handler(picture, FramesLost, recovered))
        {
            FramesLost = 0;
            references.Add(frameIndexCur);
            frameIndexPrevComplete = frameIndexCur;
        }

        frameIndexPrev = frameIndexCur;
    }

    /// <summary>
    /// Whether the frame should reach the decoder, and whether its reference had to be moved.
    ///
    /// A slice that will not parse is NOT a failure. The C derives success from the flush result
    /// alone and lets a declined frame through to the callback anyway (PP57), so the questions here
    /// are simply not asked of it - which is also what happens before a header has arrived.
    /// </summary>
    private bool Decodable(byte[] picture, out bool recovered)
    {
        recovered = false;

        if (bitstream?.ReadSlice(picture) is not { } slice)
            return true;

        if (slice.Type == BitstreamSliceType.I)
        {
            waitingForIdr = false;
            return true;
        }

        if (slice.Type != BitstreamSliceType.P)
            return true;

        // A P-frame while waiting is a difference against a frame the decoder never got.
        if (VideoFlushDecision.SkipWhileWaitingForIdr(waitingForIdr, isIntraSlice: false, out _))
            return false;

        ReferenceChoice choice = references.Choose(frameIndexCur, (int)slice.ReferenceFrame);
        if (choice.Lost)
        {
            FramesLost++;
            FramesLostTotal++;
            return false;
        }

        // The substitute is written back into the picture, which is what makes it decodable against
        // a reference the encoder did not name.
        if (!choice.Present)
            recovered = bitstream.SetReferenceFrame(picture, (uint)choice.Substitute);

        return true;
    }

}
