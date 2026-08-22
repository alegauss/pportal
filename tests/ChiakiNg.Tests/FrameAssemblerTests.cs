using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP289: the managed frame assembler, against the C one it replaces.
///
/// The same units are pushed through both in the same order, and the frame that comes out has to be
/// the same bytes with the same result code. That is the only comparison worth making here - the
/// intermediate state is private to each and a port that matched it would be a port of the struct
/// rather than of the behaviour.
///
/// Loss is what this is for, so most of these lose something. A frame that arrives whole exercises
/// the assembly and none of the repair, which is the half that was already working.
/// </summary>
public class FrameAssemblerTests(ITestOutputHelper output)
{
    /// <summary>
    /// One unit as the stream delivers it: two bytes of header, then picture.
    ///
    /// The header is the length extension in the first unit and padding everywhere - the same two
    /// bytes read two ways, which is the part of this format most likely to be got wrong once.
    /// </summary>
    private static byte[] Unit(int payload, ushort header, int seed)
    {
        var data = new byte[payload + 2];
        data[0] = (byte)(header >> 8);
        data[1] = (byte)(header & 0xff);

        var random = new Random(seed);
        random.NextBytes(data.AsSpan(2));
        return data;
    }

    /// <summary>
    /// Builds a whole frame's worth of units, parity included, by asking the C to encode it.
    ///
    /// The parity has to come from somewhere and inventing it would make every FEC case fail for a
    /// reason about the fixture. FecCodec.Encode is used because PP287 agreed it against a decoder
    /// that was itself agreed against jerasure - so this is the encode judged by the round trip,
    /// reused as a source of frames rather than as the thing under test.
    /// </summary>
    private static byte[][] BuildFrame(int source, int parity, int payload, int seed)
    {
        int unitSize = payload + 2;
        int stride = (unitSize + 0xf) / 0x10 * 0x10;

        var frame = new byte[stride * (source + parity)];
        for (int i = 0; i < source; i++)
            Unit(payload, 0, seed + i).CopyTo(frame.AsSpan(i * stride));

        FecCodec.Encode(frame, unitSize, stride, source, parity);

        var units = new byte[source + parity][];
        for (int i = 0; i < source; i++)
            units[i] = frame.AsSpan(i * stride, unitSize).ToArray();
        for (int i = 0; i < parity; i++)
            units[source + i] = frame.AsSpan((source * unitSize) + (i * unitSize), unitSize).ToArray();

        return units;
    }

    /// <summary>Runs a sequence through the managed assembler.</summary>
    private static (FrameFlushResult Result, byte[] Frame) Managed(
        byte[][] units, int source, int parity, IReadOnlyList<int> deliver)
    {
        var assembler = new FrameAssembler();
        int total = source + parity;

        int first = deliver[0];
        Assert.Equal(ChiakiError.Success,
            assembler.AllocFrame(isVideo: true, first, total, parity, units[first]));
        Assert.Equal(ChiakiError.Success, assembler.PutUnit(first, total, units[first]));

        foreach (int index in deliver.Skip(1))
            Assert.Equal(ChiakiError.Success, assembler.PutUnit(index, total, units[index]));

        FrameFlushResult result = assembler.Flush(out ReadOnlySpan<byte> frame);
        return (result, frame.ToArray());
    }

    /// <summary>And the same sequence through the C, via the shim.</summary>
    private static (FrameFlushResult Result, byte[] Frame) NativeRun(
        byte[][] units, int source, int parity, IReadOnlyList<int> deliver)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        using var processor = new FrameProcessor();
        int total = source + parity;

        int first = deliver[0];
        Assert.Equal(ChiakiError.Success, processor.AllocFrame(0, (ushort)first, (ushort)total, (ushort)parity, units[first]));
        Assert.Equal(ChiakiError.Success, processor.PutUnit(0, (ushort)first, (ushort)total, (ushort)parity, units[first]));

        foreach (int index in deliver.Skip(1))
            Assert.Equal(ChiakiError.Success, processor.PutUnit(0, (ushort)index, (ushort)total, (ushort)parity, units[index]));

        return processor.Flush();
    }

    /// <summary>
    /// THE ASSERTION. Same units in, same frame and same verdict out.
    /// </summary>
    /// <param name="lose">
    /// Which source units never arrive. The parity always does - a frame that loses parity and no
    /// data needs no repair, which is a case the flush short-circuits before any of this runs.
    /// </param>
    [Theory]
    [InlineData(4, 2, 100, new int[0])]
    [InlineData(4, 2, 100, new[] { 1 })]
    [InlineData(4, 2, 100, new[] { 0, 3 })]
    [InlineData(6, 2, 60, new[] { 2 })]
    [InlineData(8, 3, 250, new[] { 0, 5, 7 })]
    [InlineData(10, 4, 32, new[] { 1, 2, 8, 9 })]
    public void BothAssemblersProduceTheSameFrame(int source, int parity, int payload, int[] lose)
    {
        byte[][] units = BuildFrame(source, parity, payload, seed: source * 100 + parity);

        var deliver = new List<int>();
        for (int i = 0; i < source + parity; i++)
        {
            if (!lose.Contains(i))
                deliver.Add(i);
        }

        var managed = Managed(units, source, parity, deliver);
        var native = NativeRun(units, source, parity, deliver);

        Assert.Equal(native.Result, managed.Result);
        Assert.Equal(native.Frame, managed.Frame);

        output.WriteLine(
            $"{source}+{parity} at {payload}B, lost [{string.Join(",", lose)}]: "
                + $"{native.Result}, {native.Frame.Length} bytes, identical");
    }

    /// <summary>
    /// The rejections, which are behaviour too and are the cheapest thing to get subtly wrong.
    /// </summary>
    [Fact]
    public void TheSameInputsAreRefused()
    {
        var assembler = new FrameAssembler();
        byte[] unit = Unit(64, 0, 1);

        // Parity claimed above the total.
        Assert.Equal(ChiakiError.InvalidData, assembler.AllocFrame(true, 0, 2, 3, unit));

        // More slots than the C will allocate.
        Assert.Equal(ChiakiError.InvalidData,
            assembler.AllocFrame(true, 0, FrameAssembler.UnitSlotsMax + 8, 1, unit));

        // A first video unit too short to carry the length extension it is read for.
        Assert.Equal(ChiakiError.BufTooSmall, assembler.AllocFrame(true, 0, 4, 1, [0x01]));

        Assert.Equal(ChiakiError.Success, assembler.AllocFrame(true, 0, 6, 2, unit));
        Assert.Equal(ChiakiError.Success, assembler.PutUnit(0, 6, unit));

        // ...and the same unit twice.
        Assert.Equal(ChiakiError.InvalidData, assembler.PutUnit(0, 6, unit));

        // An index past the frame, and an empty unit.
        Assert.Equal(ChiakiError.InvalidData, assembler.PutUnit(9, 6, unit));
        Assert.Equal(ChiakiError.InvalidData, assembler.PutUnit(1, 6, []));
    }

    /// <summary>
    /// A flush before any frame fails, and a SECOND flush of the same frame is destructive in both.
    ///
    /// Two wrong guesses went into this before it was written down. The first was that a flush sets
    /// the flag called `flushed` - it does not; the C sets it in init and clears it in alloc_frame
    /// and nothing sets it again, so the flush is not guarded against being called twice. The
    /// second was that it is therefore idempotent. It is not either: the flush COMPACTS the picture
    /// into the front of the buffer it was assembled in, so a second one reads each unit from an
    /// offset the first has already overwritten - and returns a frame of exactly the right LENGTH
    /// made of the wrong bytes, which is worse than a short one.
    ///
    /// Both implementations do exactly that, which is what makes it reproduced rather than a bug
    /// introduced here. The C's own header says the returned pointer "MUST NOT be used after the
    /// next call to this frame processor", and its double-flush comment is about charging the
    /// reassembly once rather than about the bytes.
    ///
    /// Asserted as agreement and not as correctness: the two must degrade the same way, because a
    /// port that quietly made the second call safe would be one whose frames no longer match the C
    /// wherever a caller relies on the first.
    /// </summary>
    [Fact]
    public void ASecondFlushDegradesTheSameWayInBoth()
    {
        var assembler = new FrameAssembler();
        Assert.Equal(FrameFlushResult.Failed, assembler.Flush(out _));

        byte[][] units = BuildFrame(4, 2, 48, seed: 7);
        int[] deliver = [0, 1, 2, 3, 4, 5];

        Assert.Equal(ChiakiError.Success, assembler.AllocFrame(true, 0, 6, 2, units[0]));
        foreach (int i in deliver)
            Assert.Equal(ChiakiError.Success, assembler.PutUnit(i, 6, units[i]));

        Assert.Equal(FrameFlushResult.Success, assembler.Flush(out ReadOnlySpan<byte> managedFirst));
        var managedFirstBytes = managedFirst.ToArray();
        Assert.Equal(FrameFlushResult.Success, assembler.Flush(out ReadOnlySpan<byte> managedSecond));
        var managedSecondBytes = managedSecond.ToArray();

        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());
        using var processor = new FrameProcessor();
        Assert.Equal(ChiakiError.Success, processor.AllocFrame(0, 0, 6, 2, units[0]));
        foreach (int i in deliver)
            Assert.Equal(ChiakiError.Success, processor.PutUnit(0, (ushort)i, 6, 2, units[i]));

        var nativeFirst = processor.Flush();
        var nativeSecond = processor.Flush();

        Assert.Equal(nativeFirst.Frame, managedFirstBytes);
        Assert.Equal(nativeSecond.Result, managedSecondBytes.Length == 0
            ? FrameFlushResult.Failed
            : FrameFlushResult.Success);
        Assert.Equal(nativeSecond.Frame, managedSecondBytes);

        // Same LENGTH, different bytes - which is the sharpest form of the hazard and was the third
        // wrong guess here. The recorded unit sizes do not change, so the total is arithmetic on
        // them and comes out identical; what moved is where each unit is read FROM, because the
        // front of the buffer now holds the first flush's output. A caller checking the size would
        // see a frame exactly as long as it should be, full of the wrong bytes.
        Assert.Equal(managedFirstBytes.Length, managedSecondBytes.Length);
        Assert.NotEqual(managedFirstBytes, managedSecondBytes);
    }

    /// <summary>
    /// FlushPossible counts parity toward the source total, which is the point of carrying it.
    /// </summary>
    [Fact]
    public void ParityCountsTowardFlushability()
    {
        byte[][] units = BuildFrame(4, 2, 48, seed: 11);
        var assembler = new FrameAssembler();

        Assert.Equal(ChiakiError.Success, assembler.AllocFrame(true, 0, 6, 2, units[0]));
        Assert.False(assembler.FlushPossible);

        // Three source units and one parity: four received against four expected.
        foreach (int i in (int[])[0, 1, 2, 4])
            Assert.Equal(ChiakiError.Success, assembler.PutUnit(i, 6, units[i]));

        Assert.True(assembler.FlushPossible);
    }

    /// <summary>The byte and frame counters a caller reads back.</summary>
    [Fact]
    public void TheStreamCountersFollowTheFrames()
    {
        var assembler = new FrameAssembler();
        Assert.Equal(0UL, assembler.Bitrate(60));

        byte[][] units = BuildFrame(4, 2, 48, seed: 13);
        Assert.Equal(ChiakiError.Success, assembler.AllocFrame(true, 0, 6, 2, units[0]));
        for (int i = 0; i < 6; i++)
            Assert.Equal(ChiakiError.Success, assembler.PutUnit(i, 6, units[i]));

        assembler.Flush(out ReadOnlySpan<byte> frame);
        Assert.Equal(1UL, assembler.Frames);
        Assert.Equal((ulong)frame.Length, assembler.Bytes);
        Assert.Equal((ulong)frame.Length * 8 * 60, assembler.Bitrate(60));

        assembler.ResetStats();
        Assert.Equal(0UL, assembler.Frames);
        Assert.Equal(0UL, assembler.Bitrate(60));
    }
}
