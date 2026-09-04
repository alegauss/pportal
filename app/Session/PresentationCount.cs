namespace ChiakiNg.Session;

/// <summary>
/// PP699: the presenter's two counters, which lived in the client that no longer builds.
///
/// PP76 compares decoders by what they lose under jitter, and the number it reads is
/// <c>frames_dropped</c> less <c>frames_lost</c>. The first of those is the PRESENTER's, and PP528
/// repaired it in <c>gui/src/qmlmainwindow.cpp</c> - which PP598 retired and PP632 stopped
/// building. So the operand did not exist here, and PP700 found the deeper reason: nothing
/// presented at all.
///
/// PP700 built the presenter. This counts what it does.
///
/// THE ARITHMETIC IS THE QT CLIENT'S, TRANSCRIBED. Two things add to the dropped total and they
/// are not the same thing:
///
///   the RECEIVER's loss, folded in at the frame callback - <c>if (frames_lost > 0)
///   session_baseline.frames_dropped += frames_lost</c> - which is every frame the network lost
///   before any decoder saw it;
///
///   and the presenter's OWN discards, one per call, at seven sites in the client: a frame libplacebo
///   would not take, a stale pending frame, an overflow. Each is a frame that decoded and was never
///   shown, and that eviction is the only loss in the path a decoder is responsible for.
///
/// WHY THE DIFFERENCE IS A FLOOR AND NOT A COUNT. The two counters are sampled by different threads
/// - the receiver's total is polled, the presenter's accumulated per pull - so a session can end
/// with the receiver ahead and the difference reading low. Neither error can make it read HIGH, so
/// a difference that appears is real and an absent one is not evidence of a decoder that lost
/// nothing. sessionbaseline.h states this and it is the reason PP76 reads a floor.
///
/// PP528'S REPAIR IS THE SHAPE TO KEEP. The count comes off the decoder's pull and is zeroed by it,
/// so every path that pulls has to carry it somewhere or lose it for good. In the C, two returns
/// between the pull and the present dropped it silently. Here there is one place a frame is
/// counted and every arm reaches it.
/// </summary>
public sealed class PresentationCount
{
    private long presented;
    private long dropped;

    /// <summary>Frames that reached the screen.</summary>
    public long Presented => Interlocked.Read(ref presented);

    /// <summary>
    /// Frames the presenter never showed, the receiver's own loss folded in.
    ///
    /// This is the C's <c>frames_dropped</c>, which is why it carries both: PP76's subtraction
    /// takes the receiver's total back out again, and a counter holding only the presenter's own
    /// discards would leave that subtraction reading below zero.
    /// </summary>
    public long Dropped => Interlocked.Read(ref dropped);

    /// <summary>
    /// The decoder-attributable loss: dropped less lost, and never below zero.
    ///
    /// The clamp is the C's and is not defensive. The two counters are sampled by different threads
    /// at different moments, so the receiver can legitimately be ahead of the presenter at the end
    /// of a session - and subtracting unsigned would turn a few frames of skew into eighteen
    /// quintillion.
    /// </summary>
    public long DecoderDropsAgainst(long framesLost)
    {
        long theirs = Dropped;
        return theirs > framesLost ? theirs - framesLost : 0;
    }

    /// <summary>One frame reached the screen.</summary>
    public void Present() => Interlocked.Increment(ref presented);

    /// <summary>
    /// One frame decoded and was never shown, which is the presenter's own discard.
    ///
    /// The client's <c>increaseDroppedFrames</c>, at each of its seven call sites.
    /// </summary>
    public void Discard() => Interlocked.Increment(ref dropped);

    /// <summary>
    /// The receiver's loss for one pull, folded in.
    ///
    /// Zero and negative are no-ops rather than errors, which is the C's own guard - the pull hands
    /// back whatever it accumulated and a quiet interval accumulates nothing.
    /// </summary>
    public void Lost(int framesLost)
    {
        if (framesLost > 0)
            Interlocked.Add(ref dropped, framesLost);
    }
}
