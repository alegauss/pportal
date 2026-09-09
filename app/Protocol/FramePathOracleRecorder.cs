using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP842: walks <see cref="FramePathOracleCases"/> through the real wrappers and writes down every
/// answer.
///
/// Runs only on a build that carries the oracles, which is the point: what it produces is evidence
/// from fec.c, frameprocessor.c and videoreceiver.c rather than a table somebody typed. A build
/// without them refuses rather than writing a file of nulls, because a recording that recorded
/// nothing is worse than none - the comparisons would then run against it and pass.
///
/// PP785's shape, one oracle over. What differs is the SHAPE OF AN ANSWER: takion's are headers and
/// counts, and these are frames. The matrices are held entry for entry, the assembled frames in hex,
/// and the sixty-four decoded frames as digests - see <see cref="DecodeAnswer.Digest"/> for why that
/// one is not the bytes.
/// </summary>
public static class FramePathOracleRecorder
{
    /// <summary>What a recording attempt did.</summary>
    public enum Outcome
    {
        /// <summary>Written, and the file is the second opinion.</summary>
        Recorded,

        /// <summary>This build has no frame-path wrappers, so there was nothing to ask.</summary>
        NoOracle,

        /// <summary>test/fec_test_cases.inl was not found, so the decode had nothing to answer about.</summary>
        NoCases,

        /// <summary>The C refused cases the recording exists to hold answers for.</summary>
        OracleDisagreed,
    }

    /// <summary>
    /// Asks the C every case and returns what it said, or null where this build has no oracle.
    ///
    /// lib_init first, and it is not ceremony: jerasure's field tables are a global that
    /// galois_init_default_field fills, so a matrix asked for before it is a matrix of zeroes - which
    /// would be recorded as jerasure's answer and agreed with by nothing.
    /// </summary>
    public static FramePathOracleRecording? Take()
    {
        if (!ShimFramePathShape.OracleIsAvailable())
            return null;

        if (ChiakiSession.LibInit() != ChiakiError.Success)
            return null;

        return new FramePathOracleRecording(
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [.. FramePathOracleCases.Matrices.Select(one => AskMatrix(one.K, one.M))],
            [.. FramePathOracleCases.Decodes.Select((one, index) => AskDecode(index, one))],
            [.. FramePathOracleCases.Assemblies.Select(AskAssembly)],
            AskSecondFlush(),
            AskReceiver());
    }

    /// <summary>One shape through jerasure's own Cauchy construction.</summary>
    public static MatrixAnswer AskMatrix(int k, int m)
        => new(k, m, FecMatrix.Native(k, m) ?? []);

    /// <summary>
    /// One recorded erasure case through fec.c.
    ///
    /// The shape travels with the answer so a corpus that changed is a failure rather than a silent
    /// reindex: the comparison looks a case up by index and checks k, m and the unit size agree
    /// before it compares anything.
    /// </summary>
    public static DecodeAnswer AskDecode(int index, FecCase recorded)
    {
        byte[]? frame = Fec.Decode(recorded, managed: false);

        return new DecodeAnswer(
            index,
            recorded.K,
            recorded.M,
            recorded.UnitSize,
            string.Join(",", recorded.Erasures),
            frame?.Length ?? 0,
            frame is null ? null : Digest(frame));
    }

    /// <summary>One frame's units through frameprocessor.c, in the order they arrive.</summary>
    public static AssemblyAnswer AskAssembly(AssemblyCase one)
    {
        ArgumentNullException.ThrowIfNull(one);

        byte[][] units = FramePathOracleCases.Units(one);
        IReadOnlyList<int> deliver = FramePathOracleCases.Delivered(one);

        using var processor = new FrameProcessor();
        var total = (ushort)(one.Source + one.Parity);
        var parity = (ushort)one.Parity;
        var first = (ushort)deliver[0];

        processor.AllocFrame(0, first, total, parity, units[first]);
        processor.PutUnit(0, first, total, parity, units[first]);

        foreach (int index in deliver.Skip(1))
            processor.PutUnit(0, (ushort)index, total, parity, units[index]);

        (FrameFlushResult result, byte[] frame) = processor.Flush();
        return new AssemblyAnswer(one.Name, result, Hex(frame));
    }

    /// <summary>
    /// The same frame flushed twice, which is the hazard rather than the shape.
    ///
    /// Both flushes are recorded, because "the second returns the same length" and "the second
    /// returns different bytes" are the two halves of the finding and either alone is agreeable to
    /// an implementation that got it wrong.
    /// </summary>
    public static SecondFlushAnswer AskSecondFlush()
    {
        AssemblyCase one = FramePathOracleCases.SecondFlush;
        byte[][] units = FramePathOracleCases.Units(one, FramePathOracleCases.SecondFlushSeed);

        using var processor = new FrameProcessor();
        var total = (ushort)(one.Source + one.Parity);
        var parity = (ushort)one.Parity;

        processor.AllocFrame(0, 0, total, parity, units[0]);
        for (ushort i = 0; i < total; i++)
            processor.PutUnit(0, i, total, parity, units[i]);

        (FrameFlushResult _, byte[] firstFrame) = processor.Flush();
        (FrameFlushResult secondResult, byte[] secondFrame) = processor.Flush();

        return new SecondFlushAnswer(Hex(firstFrame), Hex(secondFrame), secondResult);
    }

    /// <summary>
    /// One whole frame through videoreceiver.c, recording every callback.
    ///
    /// The header goes to the callback before any picture does, so the list is the profile header
    /// and then the frame - and holding both is what says the ORDER is reproduced and not just the
    /// bytes of the picture.
    /// </summary>
    public static ReceiverAnswer AskReceiver()
    {
        var frames = new List<string>();

        using var receiver = new VideoReceiver(
            (frame, _, _) =>
            {
                frames.Add(Hex(frame.ToArray()));
                return true;
            },
            ChiakiCodec.H264);

        receiver.StreamInfo(FramePathOracleCases.ReceiverHeader(), 1920, 1080);

        for (ushort i = 0; i < FramePathOracleCases.ReceiverUnits; i++)
        {
            receiver.AvPacket(
                FramePathOracleCases.ReceiverFrame, i, FramePathOracleCases.ReceiverUnits, 0,
                FramePathOracleCases.Unit(FramePathOracleCases.ReceiverPayload, 0, seed: 40 + i));
        }

        return new ReceiverAnswer(frames, receiver.FramesLost);
    }

    /// <summary>Takes a recording and writes it where the comparisons read it.</summary>
    /// <param name="path">Where to write, or null for <see cref="FramePathOracleRecording.RelativePath"/>.</param>
    public static Outcome Write(string? path = null)
    {
        if (FramePathOracleCases.Decodes.Count == 0)
            return Outcome.NoCases;

        FramePathOracleRecording? taken = Take();
        if (taken is null)
            return Outcome.NoOracle;

        // A recording where nothing decoded, nothing assembled and nothing was delivered is not a
        // recording of the frame path - it is a recording of a library that answered nothing, and
        // every comparison would then agree with it. Every module, because any one of them can be
        // broken while the other two answer.
        bool decoded = taken.Decodes.Any(row => row.Digest is not null);
        bool assembled = taken.Assemblies.Any(row => row.Result == FrameFlushResult.Success)
            && taken.Assemblies.All(row => row.Frame.Length > 0);
        bool matrices = taken.Matrices.All(row => row.Entries.Count > 0 && row.Entries.All(entry => entry != 0));

        if (!decoded || !assembled || !matrices || taken.Receiver.Frames.Count == 0)
            return Outcome.OracleDisagreed;

        string destination = path ?? Destination();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(taken, FramePathOracleRecording.Format));

        return Outcome.Recorded;
    }

    /// <summary>
    /// Where the recording goes: beside the checkout's other oracles, or the working directory.
    ///
    /// The file has to be committed, so the checkout is the only useful destination - but a run from
    /// outside one should produce the file rather than throw, and say where it put it.
    /// </summary>
    public static string Destination()
        => SanitizerSource.RepositoryRoot() is { } root
            ? Path.Combine(root, FramePathOracleRecording.RelativePath)
            : Path.GetFullPath(Path.GetFileName(FramePathOracleRecording.RelativePath));

    /// <summary>SHA-256 of a frame, which is what stands in for the bytes.</summary>
    public static string Digest(byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return Convert.ToHexString(SHA256.HashData(frame)).ToLowerInvariant();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
