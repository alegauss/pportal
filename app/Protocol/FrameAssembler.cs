using System.Buffers.Binary;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP289: frameprocessor.c, in managed code.
///
/// It is the only caller of the FEC decode left in the tree, so jerasure and gf-complete stay in
/// the build for exactly as long as it does. PP286 and PP287 built a managed Reed-Solomon that
/// produces byte-identical frames to the C on all 64 recorded cases; this is what has to sit on top
/// of it before that matters.
///
/// What it does is assemble one video frame out of the units a stream delivers, repair the missing
/// ones from parity where it can, and hand back the concatenation. There are no sockets and no
/// threads in it - every input arrives as a buffer and every output is one.
///
/// Two bytes per unit that are not the picture
/// -------------------------------------------
/// Each video unit begins with a big-endian 16-bit number, and it means two different things at two
/// different moments. In the FIRST unit of a frame it is how much LONGER the widest unit is than
/// this one, which is how the frame buffer is sized before most of the units have arrived. In every
/// unit at flush time it is padding, and the picture is what follows it. The same two bytes, read
/// as a length going in and skipped as a header coming out.
///
/// What is deliberately not carried
/// --------------------------------
/// The C also pushes two timings per frame into PP41's histograms - reassemble and correct. Those
/// belong to the baseline record rather than to assembly, they need a monotonic clock this class
/// has no reason to hold, and carrying them here would make this the second place that decides what
/// a stage is. The stream byte and frame counters ARE carried, because a caller reads them back.
/// </summary>
public sealed class FrameAssembler
{
    /// <summary>The C's UNIT_SLOTS_MAX. A packet claiming more is refused rather than allocated.</summary>
    public const int UnitSlotsMax = 512;

    /// <summary>
    /// What ffmpeg reads past the end of a frame. The buffer carries it so a decoder handed the
    /// frame does not walk off it, and it is zeroed like the rest.
    /// </summary>
    public const int VideoBufferPadding = 64;

    private byte[] frameBuf = [];
    private int[] unitSlots = [];

    private int bufSizePerUnit;
    private int bufStridePerUnit;
    private int unitsSourceExpected;
    private int unitsFecExpected;
    private int unitsSourceReceived;
    private int unitsFecReceived;
    private bool flushed = true;

    /// <summary>How many whole frames have been flushed.</summary>
    public ulong Frames { get; private set; }

    /// <summary>And how many bytes of picture they carried.</summary>
    public ulong Bytes { get; private set; }

    /// <summary>chiaki_stream_stats_bitrate: zero before the first frame rather than a divide.</summary>
    public ulong Bitrate(ulong framerate) => Frames == 0 ? 0 : Bytes * 8 * framerate / Frames;

    /// <summary>chiaki_stream_stats_reset.</summary>
    public void ResetStats()
    {
        Frames = 0;
        Bytes = 0;
    }

    /// <summary>
    /// Whether enough units have arrived that the frame can be completed, with parity if needed.
    ///
    /// The C compares the TOTAL received against the source count, not the source received - so a
    /// frame whose losses are all in the data units is flushable as soon as enough parity has made
    /// up the number. That is the whole point of carrying parity.
    /// </summary>
    public bool FlushPossible => unitsSourceReceived + unitsFecReceived >= unitsSourceExpected;

    /// <summary>Begins a frame from its first unit.</summary>
    public ChiakiError AllocFrame(bool isVideo, int unitIndex, int unitsInFrameTotal, int unitsInFrameFec, ReadOnlySpan<byte> data)
    {
        if (unitsInFrameTotal < unitsInFrameFec)
            return ChiakiError.InvalidData;

        flushed = false;
        unitsSourceExpected = unitsInFrameTotal - unitsInFrameFec;
        unitsFecExpected = unitsInFrameFec;

        // Forced to at least one, which the C does without saying why: a frame with no parity still
        // needs a slot for it, because units_source_expected + units_fec_expected is what sizes the
        // slot array and the erasure walk.
        if (unitsFecExpected < 1)
            unitsFecExpected = 1;

        bufSizePerUnit = data.Length;
        if (isVideo && unitIndex < unitsSourceExpected)
        {
            if (data.Length < 2)
                return ChiakiError.BufTooSmall;

            bufSizePerUnit += BinaryPrimitives.ReadUInt16BigEndian(data);
        }

        bufStridePerUnit = (bufSizePerUnit + 0xf) / 0x10 * 0x10;

        if (bufSizePerUnit == 0)
            return ChiakiError.BufTooSmall;

        unitsSourceReceived = 0;
        unitsFecReceived = 0;

        int slotsRequired = unitsSourceExpected + unitsFecExpected;
        if (slotsRequired > UnitSlotsMax)
            return ChiakiError.InvalidData;

        if (slotsRequired != unitSlots.Length)
            unitSlots = new int[slotsRequired];
        else
            Array.Clear(unitSlots);

        int required = slotsRequired * bufStridePerUnit;
        if (frameBuf.Length < required + VideoBufferPadding)
            frameBuf = new byte[required + VideoBufferPadding];
        else
            Array.Clear(frameBuf, 0, required + VideoBufferPadding);

        return ChiakiError.Success;
    }

    /// <summary>Takes one unit into its slot.</summary>
    public ChiakiError PutUnit(int unitIndex, int unitsInFrameTotal, ReadOnlySpan<byte> data)
    {
        if (unitIndex >= unitsInFrameTotal)
            return ChiakiError.InvalidData;

        if (unitIndex >= unitSlots.Length)
            return ChiakiError.InvalidData;

        if (data.Length == 0)
            return ChiakiError.InvalidData;

        if (data.Length > bufSizePerUnit)
            return ChiakiError.InvalidData;

        // A duplicate is refused rather than overwritten, and the counters are not touched: the
        // second copy is not evidence of a second unit arriving.
        if (unitSlots[unitIndex] != 0)
            return ChiakiError.InvalidData;

        unitSlots[unitIndex] = data.Length;

        // The size is recorded even when the bytes are not. A unit arriving after the flush still
        // counts as received, which is what stops the next flush attempting FEC it does not need.
        if (!flushed)
            data.CopyTo(frameBuf.AsSpan(unitIndex * bufStridePerUnit));

        if (unitIndex < unitsSourceExpected)
            unitsSourceReceived++;
        else
            unitsFecReceived++;

        return ChiakiError.Success;
    }

    /// <summary>
    /// Completes the frame, repairing it first where units are missing.
    /// </summary>
    /// <returns>The picture, or an empty span with a failed result.</returns>
    public FrameFlushResult Flush(out ReadOnlySpan<byte> frame)
    {
        frame = default;

        if (unitsSourceExpected == 0 || flushed)
            return FrameFlushResult.Failed;

        var result = FrameFlushResult.Success;
        if (unitsSourceReceived < unitsSourceExpected)
            result = Correct() ? FrameFlushResult.FecSuccess : FrameFlushResult.FecFailed;

        // The picture is compacted into the FRONT of the same buffer it was assembled in, over the
        // units already read. That works only because cur never overtakes the read position - each
        // unit contributes stride bytes of space and at most stride-2 bytes of picture.
        int cur = 0;
        for (int i = 0; i < unitsSourceExpected; i++)
        {
            int size = unitSlots[i];
            if (size == 0)
                continue;

            // Under two bytes is not a short unit, it is a unit with no room for the header the
            // next line skips. The C logs and drops it, and so does this.
            if (size < 2)
                continue;

            int partSize = size - 2;
            frameBuf.AsSpan((i * bufStridePerUnit) + 2, partSize).CopyTo(frameBuf.AsSpan(cur));
            cur += partSize;
        }

        Frames++;
        Bytes += (ulong)cur;

        frame = frameBuf.AsSpan(0, cur);
        return result;
    }

    /// <summary>
    /// The FEC pass: name the empty slots, decode, and read each recovered unit's length back out.
    /// </summary>
    private bool Correct()
    {
        int expected = unitsSourceExpected + unitsFecExpected;
        int received = unitsSourceReceived + unitsFecReceived;

        var erasures = new uint[expected - received];
        int at = 0;
        for (int i = 0; i < expected; i++)
        {
            if (unitSlots[i] != 0)
                continue;

            // The C asserts this cannot overrun and checks anyway. Here it is a refusal: the slot
            // array and the counters disagreeing means one of them is wrong, and decoding against a
            // short erasure list would repair the wrong units.
            if (at >= erasures.Length)
                return false;

            erasures[at++] = (uint)i;
        }

        if (at != erasures.Length)
            return false;

        if (!FecCodec.Decode(frameBuf, bufSizePerUnit, bufStridePerUnit, unitsSourceExpected, unitsFecExpected, erasures))
            return false;

        // A recovered unit has bytes but no recorded size, and its size is in the two bytes FEC
        // just rebuilt: the padding, counted back off the whole unit. A unit whose padding claims
        // to be the entire unit is left with no size at all rather than a negative one - the C
        // logs and continues, which leaves the slot empty and the unit dropped at flush.
        for (int i = 0; i < unitsSourceExpected; i++)
        {
            ushort padding = BinaryPrimitives.ReadUInt16BigEndian(frameBuf.AsSpan(i * bufStridePerUnit, 2));
            if (padding >= bufSizePerUnit)
                continue;

            unitSlots[i] = bufSizePerUnit - padding;
        }

        return true;
    }
}
