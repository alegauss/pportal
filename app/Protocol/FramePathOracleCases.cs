namespace ChiakiNg.Protocol;

/// <summary>One shape both matrix constructions are asked for.</summary>
/// <param name="K">Source units.</param>
/// <param name="M">Parity units.</param>
public readonly record struct MatrixCase(int K, int M);

/// <summary>One frame pushed through both assemblers.</summary>
/// <param name="Name">What a failure names, so it is a shape and not an index.</param>
/// <param name="Source">Source units.</param>
/// <param name="Parity">Parity units.</param>
/// <param name="Payload">Picture bytes per unit, on top of the two-byte header.</param>
/// <param name="Lose">
/// Which source units never arrive. The parity always does: a frame that loses parity and no data
/// needs no repair, and the flush short-circuits before any of the interesting code runs.
/// </param>
public sealed record AssemblyCase(
    string Name, int Source, int Parity, int Payload, IReadOnlyList<int> Lose);

/// <summary>
/// PP842: the inputs the frame path's oracles are asked about, so a recording of their answers is
/// a recording of something named rather than of whatever a test happened to build.
///
/// PP785's <see cref="TakionOracleCases"/> one seam over, and the reason is the same: the cases have
/// to live where BOTH the recorder and the comparison can reach them. A case built inside a test
/// method is a case the recorder cannot ask the C about.
///
/// THE FRAMES ARE BUILT BY THE MANAGED ENCODER, which sounds circular and is not. PP287 agreed
/// FecCodec's decode against jerasure's and then agreed the encode by round trip through that
/// decode, so the encode is judged elsewhere and reused here as a source of frames. What the
/// recording holds is what the C's ASSEMBLER and DECODER did with them.
/// </summary>
public static class FramePathOracleCases
{
    /// <summary>
    /// The shapes a stream actually uses, plus the degenerate one.
    ///
    /// FecMatrixTests' own list, because a matrix right for 4x2 and wrong for 15x4 is a matrix that
    /// fails on exactly the frames that needed FEC most.
    /// </summary>
    public static IReadOnlyList<MatrixCase> Matrices { get; } =
    [
        new(1, 1), new(2, 1), new(4, 2), new(8, 2),
        new(10, 3), new(15, 4), new(16, 8), new(32, 16),
    ];

    /// <summary>
    /// The assembly cases, most of them lossy.
    ///
    /// A frame that arrives whole exercises the assembly and none of the repair, which is the half
    /// that was never in doubt - so one case is whole and the rest lose between one and four units.
    /// </summary>
    public static IReadOnlyList<AssemblyCase> Assemblies { get; } =
    [
        new("4+2 whole", 4, 2, 100, []),
        new("4+2 lost 1", 4, 2, 100, [1]),
        new("4+2 lost 0,3", 4, 2, 100, [0, 3]),
        new("6+2 lost 2", 6, 2, 60, [2]),
        new("8+3 lost 0,5,7", 8, 3, 250, [0, 5, 7]),
        new("10+4 lost 1,2,8,9", 10, 4, 32, [1, 2, 8, 9]),
    ];

    /// <summary>The case the double flush is asked about, which is a hazard rather than a shape.</summary>
    public static AssemblyCase SecondFlush { get; } = new("4+2 flushed twice", 4, 2, 48, []);

    /// <summary>The seed that case is built with, kept separate because the frame has to be identical.</summary>
    public const int SecondFlushSeed = 7;

    /// <summary>Source units in the video receiver's case.</summary>
    public const int ReceiverUnits = 4;

    /// <summary>Picture bytes per unit in it.</summary>
    public const int ReceiverPayload = 96;

    /// <summary>
    /// The frame index the receiver's case uses, and it is not a free choice.
    ///
    /// Frame 1 is the one index that skips the corrupt-frame report, which is what makes the case
    /// runnable at all: the C's receiver reaches for a stream connection to report on, and the shim
    /// hands it a zeroed session. Any other index reports and dereferences nothing.
    /// </summary>
    public const ushort ReceiverFrame = 1;

    /// <summary>The profile header both receivers are given, which reaches the callback first.</summary>
    public static byte[] ReceiverHeader() => [0x00, 0x00, 0x00, 0x01, 0x67, 0x42];

    /// <summary>The recorded FEC cases, or an empty list outside a checkout.</summary>
    public static IReadOnlyList<FecCase> Decodes { get; } =
        FecVectors.Locate() is { } file ? FecVectors.Parse(file) : [];

    /// <summary>
    /// One unit as the stream delivers it: two bytes of header, then picture.
    ///
    /// The header is the length extension in the first unit and padding everywhere else - the same
    /// two bytes read two ways, which is the part of this format most likely to be got wrong once.
    /// </summary>
    public static byte[] Unit(int payload, ushort header, int seed)
    {
        var data = new byte[payload + 2];
        data[0] = (byte)(header >> 8);
        data[1] = (byte)(header & 0xff);

        new Random(seed).NextBytes(data.AsSpan(2));
        return data;
    }

    /// <summary>The units of one assembly case, parity included.</summary>
    public static byte[][] Units(AssemblyCase one, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(one);

        return Frame(one.Source, one.Parity, one.Payload, seed ?? SeedOf(one));
    }

    /// <summary>Which units arrive, in the order they arrive in.</summary>
    public static IReadOnlyList<int> Delivered(AssemblyCase one)
    {
        ArgumentNullException.ThrowIfNull(one);

        var deliver = new List<int>();
        for (int i = 0; i < one.Source + one.Parity; i++)
        {
            if (!one.Lose.Contains(i))
                deliver.Add(i);
        }

        return deliver;
    }

    /// <summary>
    /// The seed a case's frame is built with, derived from the shape.
    ///
    /// Derived rather than listed so a case cannot be added without one, and stated here because
    /// the recorder and the comparison have to build the SAME bytes - a seed that moved would make
    /// every recorded answer look like a disagreement.
    /// </summary>
    public static int SeedOf(AssemblyCase one)
    {
        ArgumentNullException.ThrowIfNull(one);

        return (one.Source * 100) + one.Parity;
    }

    /// <summary>The stride units are laid out at, which is the C's sixteen-byte rounding.</summary>
    public static int StrideFor(int unitSize) => (unitSize + 0xf) / 0x10 * 0x10;

    private static byte[][] Frame(int source, int parity, int payload, int seed)
    {
        int unitSize = payload + 2;
        int stride = StrideFor(unitSize);

        var frame = new byte[stride * (source + parity)];
        for (int i = 0; i < source; i++)
            Unit(payload, 0, seed + i).CopyTo(frame.AsSpan(i * stride));

        FecCodec.Encode(frame, unitSize, stride, source, parity);

        var units = new byte[source + parity][];
        for (int i = 0; i < source; i++)
            units[i] = frame.AsSpan(i * stride, unitSize).ToArray();

        // The encode writes parity PACKED at source*unitSize, not at stride, which is where the C
        // puts it. Read from there rather than repaired, because reproducing that is PP30's job.
        for (int i = 0; i < parity; i++)
            units[source + i] = frame.AsSpan((source * unitSize) + (i * unitSize), unitSize).ToArray();

        return units;
    }
}
