using System.Text.Json;
using System.Text.Json.Serialization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What jerasure built for one shape.</summary>
/// <param name="K">Source units.</param>
/// <param name="M">Parity units.</param>
/// <param name="Entries">
/// Every entry of the coding matrix, row-major and in full.
///
/// In full rather than digested because this is the one answer small enough to read: a disagreement
/// here is one entry out of a few hundred, and the interesting question is always WHICH.
/// </param>
public sealed record MatrixAnswer(int K, int M, IReadOnlyList<int> Entries);

/// <summary>What fec.c made of one recorded erasure case.</summary>
/// <param name="Index">Its position in test/fec_test_cases.inl.</param>
/// <param name="K">Source units, recorded so a changed corpus is a failure and not a silent reindex.</param>
/// <param name="M">Parity units, for the same reason.</param>
/// <param name="UnitSize">The unit's length, for the same reason.</param>
/// <param name="Erasures">Which units were blanked, comma separated.</param>
/// <param name="Length">The decoded frame's length, or zero where the C refused.</param>
/// <param name="Digest">
/// SHA-256 of the whole decoded frame, parity units included, or null where the C refused.
///
/// A DIGEST AND NOT THE BYTES, which is the one place this recording departs from PP785's. Sixty-four
/// frames at a real stream's unit sizes is over a megabyte, and hex would make this file bigger than
/// everything else in tests/oracles put together. What the comparison needs is equality, which a
/// digest answers exactly; what it gives up is being able to say where two frames diverge, and the
/// live differential is still there to answer that on any build that has the C.
/// </param>
public readonly record struct DecodeAnswer(
    int Index, uint K, uint M, int UnitSize, string Erasures, int Length, string? Digest);

/// <summary>What frameprocessor.c made of one frame's units.</summary>
/// <param name="Name">The case, by <see cref="AssemblyCase.Name"/>.</param>
/// <param name="Result">The flush's verdict.</param>
/// <param name="Frame">The whole frame the flush produced, in hex.</param>
public sealed record AssemblyAnswer(string Name, FrameFlushResult Result, string Frame);

/// <summary>
/// What frameprocessor.c does when the same frame is flushed twice.
///
/// Held because the second flush is not idempotent and not safe: it compacts the picture into the
/// front of the buffer it was assembled in, so the second reads each unit from an offset the first
/// has already overwritten - and returns a frame of exactly the right LENGTH made of the wrong
/// bytes. A port that quietly made the second call safe would stop matching the C wherever a caller
/// relies on the first.
/// </summary>
/// <param name="First">The frame the first flush produced, in hex.</param>
/// <param name="Second">The frame the second produced, in hex - same length, different bytes.</param>
/// <param name="SecondResult">What the second flush returned.</param>
public sealed record SecondFlushAnswer(string First, string Second, FrameFlushResult SecondResult);

/// <summary>What videoreceiver.c handed its callback.</summary>
/// <param name="Frames">Every callback's bytes in order, in hex - the profile header first, then the picture.</param>
/// <param name="FramesLost">chiaki_video_receiver_get_frames_lost_total afterwards.</param>
public sealed record ReceiverAnswer(IReadOnlyList<string> Frames, int FramesLost);

/// <summary>Everything the frame path's oracles were asked, and everything they said.</summary>
/// <param name="Recorded">When the run happened, so a reader can tell how old the second opinion is.</param>
/// <param name="Matrices">jerasure's coding matrices, shape by shape.</param>
/// <param name="Decodes">fec.c's decode, over the sixty-four recorded erasure cases.</param>
/// <param name="Assemblies">frameprocessor.c's assembly, case by case.</param>
/// <param name="SecondFlush">And what a second flush of one of them does.</param>
/// <param name="Receiver">videoreceiver.c's callbacks for one whole frame.</param>
public sealed record FramePathOracleRecording(
    string Recorded,
    IReadOnlyList<MatrixAnswer> Matrices,
    IReadOnlyList<DecodeAnswer> Decodes,
    IReadOnlyList<AssemblyAnswer> Assemblies,
    SecondFlushAnswer SecondFlush,
    ReceiverAnswer Receiver)
{
    /// <summary>
    /// PP842: the second opinion, written down so the frame path's three files can leave without
    /// taking it.
    ///
    /// PP670 prepared the SHAPE of that departure - the define, the export that answers for the DLL,
    /// the fourteen wrappers guarded in the header and the body, the allowance NativeSeam derives -
    /// and prepared no recording at all. So the flip was ready to remove three C files and five
    /// differentials in the same commit, and the five would have reported it as green: an early
    /// return in xUnit is a pass.
    ///
    /// PP27 IS WHAT MADE IT URGENT. Its deletion leaves libchiaki.a short two symbols that only
    /// videoreceiver.c calls, so takion cannot go while the video receiver stays - and the video
    /// receiver cannot go while its oracle is the only thing holding ManagedVideoReceiver to the C.
    /// The frame path is not a neighbour of that deletion; it is inside it.
    ///
    /// PP33 ANSWERED THIS FOR json-c AND THE ANSWER TRANSFERS. Keeping the C for a comparison nobody
    /// runs by default is what PP623's order decided against; deleting it and losing the comparison
    /// spends a reference implementation to save three files. Recording costs neither, because
    /// nothing in this tree develops fec.c and the inputs are ones this port chose.
    ///
    /// WHAT KEEPS IT FROM BECOMING A FOSSIL is the guard json-c's and takion's both have: a build
    /// that still HAS the oracle re-derives this and compares, so a recording that drifted fails on
    /// the one configuration that can tell.
    /// </summary>
    public const string RelativePath = @"tests\oracles\framepath.json";

    /// <summary>The recording, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Indented and with the verdicts spelled out, because this file is read by people too.</summary>
    public static JsonSerializerOptions Format { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The recording as it sits in the tree, or null where it is absent or unreadable.</summary>
    public static FramePathOracleRecording? Read()
    {
        if (Locate() is not { } path)
            return null;

        try
        {
            return JsonSerializer.Deserialize<FramePathOracleRecording>(File.ReadAllText(path), Format);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>The matrix for one shape, or null where the recording does not hold it.</summary>
    public MatrixAnswer? Matrix(int k, int m)
        => Matrices.FirstOrDefault(one => one.K == k && one.M == m);

    /// <summary>The decode's answer for one case, or null where the recording does not hold it.</summary>
    public DecodeAnswer? Decode(int index)
        => Decodes.Any(one => one.Index == index)
            ? Decodes.First(one => one.Index == index)
            : null;

    /// <summary>The assembly of that name, or null where the recording does not hold it.</summary>
    public AssemblyAnswer? Assembly(string name)
        => Assemblies.FirstOrDefault(one => one.Name == name);
}
