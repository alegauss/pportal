namespace ChiakiNg.Protocol;

/// <summary>What to do about a P-frame whose reference frame is missing.</summary>
/// <param name="Present">The reference the slice asked for is held; nothing to do.</param>
/// <param name="Substitute">A reference index to rewrite the slice with, or -1 where none was found.</param>
/// <param name="Lost">No substitute exists, so the frame cannot be decoded and counts as lost.</param>
public readonly record struct ReferenceChoice(bool Present, int Substitute, bool Lost);

/// <summary>
/// PP291: the sixteen reference frames videoreceiver.c keeps, and what it does when one is gone.
///
/// A P-frame is a difference against an earlier frame. If that earlier frame never arrived, the
/// decoder cannot use it - but the sixteen most recent ARE held, so the receiver looks further back
/// for one it does have and rewrites the slice to point at that instead. The picture is wrong in a
/// way a viewer sees as a smear rather than as a freeze, which is the trade the C chose.
///
/// The ring fills backwards, and that is not a detail
/// --------------------------------------------------
/// add_ref_frame has two halves. Once slot 0 is occupied every new frame shifts the whole array
/// down one and takes slot 0, dropping what was at 15 - an ordinary most-recent-first ring. Before
/// that it scans from 15 DOWNWARD and takes the first empty slot it meets, so the first frame lands
/// at 15, the second at 14, and slot 0 is filled last.
///
/// The order matters because the substitution search below walks candidates by DISTANCE from the
/// current frame rather than by slot, and <see cref="Holds"/> is a linear search that does not care
/// where a frame sits. So the fill order changes nothing about which frames are held - and a port
/// that "tidied" it into a plain ring would hold a different SET after the first sixteen frames,
/// because the shift only starts once slot 0 is taken.
/// </summary>
public sealed class ReferenceFrames
{
    /// <summary>How many the C keeps, and the ceiling on the substitution search.</summary>
    public const int Capacity = 16;

    /// <summary>The empty marker. The C memsets -1 across the array.</summary>
    public const int Empty = -1;

    /// <summary>The slice's own "no reference" value, which is never searched for.</summary>
    public const int NoReference = 0xff;

    private readonly int[] frames = CreateEmpty();

    private static int[] CreateEmpty()
    {
        var array = new int[Capacity];
        Array.Fill(array, Empty);
        return array;
    }

    /// <summary>The slots, most-recent-first once the ring has filled. For assertions.</summary>
    public IReadOnlyList<int> Slots => frames;

    /// <summary>Whether a frame index is one of the sixteen held.</summary>
    public bool Holds(int frame) => Array.IndexOf(frames, frame) >= 0;

    /// <summary>Puts the array back to empty, as init does.</summary>
    public void Reset() => Array.Fill(frames, Empty);

    /// <summary>add_ref_frame, both halves of it.</summary>
    public void Add(int frame)
    {
        if (frames[0] != Empty)
        {
            Array.Copy(frames, 0, frames, 1, Capacity - 1);
            frames[0] = frame;
            return;
        }

        for (int i = Capacity - 1; i >= 0; i--)
        {
            if (frames[i] == Empty)
            {
                frames[i] = frame;
                return;
            }
        }
    }

    /// <summary>
    /// What to do about a slice that references <paramref name="referenceFrame"/> frames back.
    /// </summary>
    /// <param name="frameIndexCur">The frame being decoded.</param>
    /// <param name="referenceFrame">
    /// The slice's reference index, counted backwards. <see cref="NoReference"/> means the slice
    /// names none, and the C skips the whole question for it rather than searching for frame
    /// cur-256.
    /// </param>
    public ReferenceChoice Choose(int frameIndexCur, int referenceFrame)
    {
        if (referenceFrame == NoReference)
            return new ReferenceChoice(Present: true, Substitute: -1, Lost: false);

        // Truncated to 16 bits, because that is what the C does: ref_frame_index is a
        // ChiakiSeqNum16 and the subtraction wraps into it. On frame 2 referencing 5 back, the
        // answer is 65533 and not -4, and 65533 is what a held index would have been stored as.
        var wanted = (ushort)(frameIndexCur - referenceFrame - 1);
        if (Holds(wanted))
            return new ReferenceChoice(Present: true, Substitute: -1, Lost: false);

        // Further back only, never nearer: a reference the slice did not ask for is acceptable
        // only if it is OLDER, because a newer one has not been decoded yet.
        for (int i = referenceFrame + 1; i < Capacity; i++)
        {
            var candidate = (ushort)(frameIndexCur - i - 1);
            if (Holds(candidate))
                return new ReferenceChoice(Present: false, Substitute: i, Lost: false);
        }

        return new ReferenceChoice(Present: false, Substitute: -1, Lost: true);
    }
}
