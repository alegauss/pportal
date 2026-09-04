namespace ChiakiNg.Session;

/// <summary>
/// PP76: the arithmetic that puts every decoded frame in exactly one column.
///
/// PP76 compares decoders by what they lose, so a residue in the accounting is read as loss and
/// attributed to whichever decoder was running. Three separate attempts left one, and none of them
/// was a decoder losing anything:
///
///   ONE SUBTRACTED FROM A PULL THAT RETURNED NOTHING. The drain keeps the last frame and discards
///   the rest, so the discards are the difference less one - but a codec that has not filled its
///   reorder window returns nothing at all for the first several packets, and charging those pulls
///   for a frame they never handed over left five frames a session in no column. Constant, and
///   small enough to read as rounding.
///
///   TWO CLOCKS SUBTRACTED FROM EACH OTHER. The difference was then taken against the
///   frame-available callback, which libchiaki increments on its own thread. Sampled before the
///   drain, a frame arriving mid-drain is returned without ever entering a difference; sampled
///   after, the same frame is charged as swallowed and then returned by the next pull, counted
///   twice. Measured, that leaked in whichever direction the sampling leaned - a frame or two a
///   session, and it grew with the session rather than sitting at a boundary.
///
/// The counter that closes is the codec's own <c>frame_num</c>, which advances only inside the
/// drain and so only on the thread that reads it. <see cref="Swallowed"/> is that subtraction, and
/// <see cref="Residue"/> is the identity it makes true.
///
/// WHY THE RESIDUE IS REPORTED RATHER THAN ASSERTED. It is zero on a correct session and the point
/// is that it stays zero, but a session that ends mid-pull can legitimately end one frame apart -
/// and a comparison run that refused to record itself over one frame would lose the reading it
/// came for. So it is printed, where a reader sees it, and it is not a failure.
/// </summary>
public static class FrameLedger
{
    /// <summary>
    /// The frames one pull's drain threw away.
    ///
    /// <paramref name="consumed"/> and <paramref name="consumedBefore"/> are the codec's own
    /// running total of frames handed back, either side of the drain. One is subtracted ONLY when
    /// one was returned, which is the distinction the first attempt was missing.
    /// </summary>
    public static int Swallowed(long consumed, long consumedBefore, bool returnedOne)
    {
        long swallowed = consumed - consumedBefore - (returnedOne ? 1 : 0);
        return swallowed > 0 ? (int)swallowed : 0;
    }

    /// <summary>
    /// What is left over once every decoded frame is placed, which should be nothing.
    ///
    /// <paramref name="dropped"/> is the C's <c>frames_dropped</c> and so carries the receiver's
    /// loss folded in - see <see cref="PresentationCount.Dropped"/> - which is why
    /// <paramref name="lost"/> is added back rather than subtracted. Those frames never reached a
    /// decoder and are not part of what a decoder produced.
    /// </summary>
    public static long Residue(long decoded, long shown, long dropped, long lost) =>
        decoded - shown - dropped + lost;

    /// <summary>The seam where the swallowed count is actually taken.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The function in it whose arithmetic this file transcribes.</summary>
    public const string PullFunction = "chiaki_shim_decoder_pull";

    /// <summary>The producer counter, which is the one that cannot close.</summary>
    public const string ProducerCounter = "frames_available";

    /// <summary>The codec's own consumer counter, which is the one that can.</summary>
    public const string ConsumerCounter = "frame_num";

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// Whether the pull takes its swallowed count off the consumer counter and not the producer's.
    ///
    /// THE ONE THING A MANAGED TEST CAN CHECK, and the one that was got wrong. The arithmetic
    /// itself is transcribed into <see cref="Swallowed"/> where it can be exercised, but which
    /// counter it is fed lives in C and needs a running console to show itself - it read as a
    /// frame or two of decoder loss, which is exactly the quantity PP76 is trying to measure.
    ///
    /// Comments are stripped first, so the paragraph in the shim explaining why the producer
    /// counter was abandoned does not read as the shim still using it.
    /// </summary>
    public static bool PullReadsTheConsumerCounter(string shimSource)
    {
        ArgumentNullException.ThrowIfNull(shimSource);

        if (CFunction.Body(CCall.Code(shimSource), PullFunction) is not { } body)
            return false;

        return body.Contains(ConsumerCounter, StringComparison.Ordinal)
            && !body.Contains(ProducerCounter, StringComparison.Ordinal);
    }
}
