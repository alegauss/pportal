using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP842: the frame path's three managed modules against what the C answered, on EVERY build.
///
/// PP670 made every frame-path differential ASK before it calls, which stops PP295's flip turning
/// them red. It does not stop them going quiet: an early return in xUnit is a pass, and the five
/// differentials over fec.c, frameprocessor.c and videoreceiver.c would report the same green with
/// nothing compared. PP785 saw that for takion and recorded its answers; nobody did it here.
///
/// PP27 IS WHY IT MATTERS NOW rather than eventually. Taking takion's nine files out leaves
/// libchiaki.a short exactly two symbols - stream_connection_send_corrupt_frame and its idr_request
/// twin - and videoreceiver.c is the only caller. So the video receiver has to leave with takion,
/// its oracle sits in one guarded region with the frame processor's, and the whole frame path goes
/// quiet in a commit that looks like it is about the transport.
///
/// SO THE ANSWERS ARE RECORDED. tests/oracles/framepath.json holds what the C said for the cases in
/// <see cref="FramePathOracleCases"/>, taken by --record-framepath-oracle while the wrappers were
/// here, and this file compares the managed side against it with no guard at all. What declines on
/// the day of the flip is the LIVE second opinion - one test at the bottom of this file, which is
/// what keeps the recording from becoming a table somebody typed.
/// </summary>
public class RecordedFramePathOracleTests
{
    /// <summary>The recording, or a failure that says how to take one.</summary>
    private static FramePathOracleRecording Recording()
    {
        FramePathOracleRecording? recording = FramePathOracleRecording.Read();

        Assert.True(
            recording is not null,
            $"{FramePathOracleRecording.RelativePath} is missing or unreadable, so the frame path's "
                + "answers are gone and nothing is comparing this port against them. Build a shim "
                + "with CHIAKI_SHIM_HAVE_FRAMEPATH and run --record-framepath-oracle.");

        return recording;
    }

    /// <summary>The matrix shapes, so a failure names one rather than an index.</summary>
    public static TheoryData<int, int> Shapes()
    {
        var data = new TheoryData<int, int>();

        foreach (MatrixCase one in FramePathOracleCases.Matrices)
            data.Add(one.K, one.M);

        return data;
    }

    /// <summary>The assembly cases, by name.</summary>
    public static TheoryData<string> Assemblies()
    {
        var data = new TheoryData<string>();

        foreach (AssemblyCase one in FramePathOracleCases.Assemblies)
            data.Add(one.Name);

        return data;
    }

    /// <summary>The recorded erasure cases, by index.</summary>
    public static TheoryData<int> DecodeIndices()
    {
        var data = new TheoryData<int>();

        for (int i = 0; i < FramePathOracleCases.Decodes.Count; i++)
            data.Add(i);

        return data;
    }

    /// <summary>
    /// PP842: the recording holds an answer for every case, and the answers are not all refusals.
    ///
    /// PP271's rule as it applies to a recorded oracle: a file of nulls is one every comparison below
    /// would agree with, and a comparison that agrees with anything asserts nothing. The recorder
    /// refuses to write one, and this is the same claim made about the file that is in the tree.
    /// </summary>
    [Fact]
    public void TheRecordingAnswersEveryCaseAndNotOnlyWithRefusals()
    {
        FramePathOracleRecording recording = Recording();

        Assert.Equal(FramePathOracleCases.Matrices.Count, recording.Matrices.Count);
        Assert.Equal(FramePathOracleCases.Assemblies.Count, recording.Assemblies.Count);

        // The corpus is a file, so a checkout that has it must be recorded for all of it and one
        // without it compares nothing - either way the two counts agree.
        Assert.Equal(FramePathOracleCases.Decodes.Count, recording.Decodes.Count);

        Assert.All(recording.Matrices, row => Assert.DoesNotContain(0, row.Entries));
        Assert.Contains(recording.Decodes, row => row.Digest is not null);
        Assert.Contains(recording.Assemblies, row => row.Result == FrameFlushResult.FecSuccess);
        Assert.Contains(recording.Assemblies, row => row.Result == FrameFlushResult.Success);

        // The header and then the picture, which is two callbacks and not one.
        Assert.Equal(2, recording.Receiver.Frames.Count);

        // And the second flush produced a frame of the same length made of different bytes, which is
        // the hazard this row exists for - a recording where they matched would be agreeable to a
        // port that had quietly made the second call safe.
        Assert.Equal(recording.SecondFlush.First.Length, recording.SecondFlush.Second.Length);
        Assert.NotEqual(recording.SecondFlush.First, recording.SecondFlush.Second);
    }

    /// <summary>
    /// PP842: the managed Cauchy matrix is the one jerasure built, entry for entry.
    ///
    /// PP286's differential, kept. The field underneath is asserted on its own laws by FecMatrixTests
    /// on every build, so what this adds is the construction - and a wrong primitive polynomial gives
    /// a perfectly consistent field whose products are simply different.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheManagedMatrixIsWhatJerasureBuilt(int k, int m)
    {
        MatrixAnswer? recorded = Recording().Matrix(k, m);
        Assert.True(recorded is not null, $"no recorded matrix for {k}x{m}");

        int[] managed = FecMatrix.Cauchy(k, m);
        Assert.Equal(recorded!.Entries.Count, managed.Length);

        for (int i = 0; i < managed.Length; i++)
        {
            Assert.True(
                recorded.Entries[i] == managed[i],
                $"{k}x{m} entry {i} (row {i / k}, column {i % k}): "
                    + $"jerasure recorded {recorded.Entries[i]}, managed {managed[i]}");
        }
    }

    /// <summary>
    /// PP842: the managed decoder produces the frame fec.c produced, parity units included.
    ///
    /// PP287's point about why the verdict is not enough: a decoder that repairs nothing and reports
    /// success agrees with a correct one on every recorded case, because the bytes it is asked about
    /// are already in the buffer. The parity is the part the stream never reads back and therefore
    /// the part nothing else in the suite would notice being rebuilt differently.
    ///
    /// The shape is checked before the bytes are, so a corpus that changed under this file is a
    /// failure naming that rather than sixty-four digests disagreeing for no stated reason.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecodeIndices))]
    public void TheManagedDecoderProducesTheFrameTheCProduced(int index)
    {
        DecodeAnswer? recorded = Recording().Decode(index);
        Assert.True(recorded is not null, $"no recorded decode for case {index}");

        DecodeAnswer was = recorded!.Value;
        FecCase one = FramePathOracleCases.Decodes[index];

        Assert.True(
            was.K == one.K && was.M == one.M && was.UnitSize == one.UnitSize
                && was.Erasures == string.Join(",", one.Erasures),
            $"case {index} is now {one.K}x{one.M} at {one.UnitSize} bytes losing "
                + $"[{string.Join(",", one.Erasures)}] and was recorded as {was.K}x{was.M} at "
                + $"{was.UnitSize} losing [{was.Erasures}] - the corpus moved under the recording");

        byte[]? managed = Fec.Decode(one, managed: true);

        if (was.Digest is null)
        {
            Assert.Null(managed);
            return;
        }

        Assert.True(managed is not null, $"the managed decoder refused case {index}, which the C did not");
        Assert.Equal(was.Length, managed.Length);
        Assert.Equal(was.Digest, FramePathOracleRecorder.Digest(managed));
    }

    /// <summary>
    /// PP842: the managed assembler produces the frame frameprocessor.c produced, and the same verdict.
    ///
    /// PP289's differential. Both halves, because the verdict alone cannot tell FEC that ran from FEC
    /// that was not needed, and the bytes alone cannot tell a repaired frame from a lucky one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Assemblies))]
    public void TheManagedAssemblerProducesTheFrameTheCProduced(string name)
    {
        AssemblyCase one = FramePathOracleCases.Assemblies.Single(row => row.Name == name);

        AssemblyAnswer? recorded = Recording().Assembly(name);
        Assert.True(recorded is not null, $"no recorded assembly for \"{name}\"");

        byte[][] units = FramePathOracleCases.Units(one);
        IReadOnlyList<int> deliver = FramePathOracleCases.Delivered(one);

        var assembler = new FrameAssembler();
        int total = one.Source + one.Parity;
        int first = deliver[0];

        Assert.Equal(
            ChiakiError.Success,
            assembler.AllocFrame(isVideo: true, first, total, one.Parity, units[first]));
        Assert.Equal(ChiakiError.Success, assembler.PutUnit(first, total, units[first]));

        foreach (int index in deliver.Skip(1))
            Assert.Equal(ChiakiError.Success, assembler.PutUnit(index, total, units[index]));

        FrameFlushResult result = assembler.Flush(out ReadOnlySpan<byte> frame);

        Assert.Equal(recorded!.Result, result);
        Assert.Equal(recorded.Frame, Hex(frame.ToArray()));
    }

    /// <summary>
    /// PP842: and a second flush degrades the way the C's did.
    ///
    /// Asserted as agreement and not as correctness. The flush COMPACTS the picture into the front of
    /// the buffer it was assembled in, so a second one reads each unit from an offset the first has
    /// already overwritten and returns a frame of exactly the right LENGTH made of the wrong bytes.
    /// A port that quietly made the second call safe would stop matching the C wherever a caller
    /// relies on the first, so what is held is that both are wrong in the same way.
    /// </summary>
    [Fact]
    public void ASecondFlushDegradesTheWayTheCDid()
    {
        SecondFlushAnswer was = Recording().SecondFlush;

        AssemblyCase one = FramePathOracleCases.SecondFlush;
        byte[][] units = FramePathOracleCases.Units(one, FramePathOracleCases.SecondFlushSeed);

        var assembler = new FrameAssembler();
        int total = one.Source + one.Parity;

        Assert.Equal(
            ChiakiError.Success,
            assembler.AllocFrame(isVideo: true, 0, total, one.Parity, units[0]));

        for (int i = 0; i < total; i++)
            Assert.Equal(ChiakiError.Success, assembler.PutUnit(i, total, units[i]));

        Assert.Equal(FrameFlushResult.Success, assembler.Flush(out ReadOnlySpan<byte> firstFrame));
        string first = Hex(firstFrame.ToArray());

        FrameFlushResult secondResult = assembler.Flush(out ReadOnlySpan<byte> secondFrame);
        string second = Hex(secondFrame.ToArray());

        Assert.Equal(was.First, first);
        Assert.Equal(was.SecondResult, secondResult);
        Assert.Equal(was.Second, second);
    }

    /// <summary>
    /// PP842: the managed receiver hands its callback what videoreceiver.c handed its own.
    ///
    /// PP291's differential. The profile header first and then the picture, in order, because the
    /// order is behaviour: a receiver that delivered the picture before the header would agree on
    /// every byte and produce a stream no decoder can start.
    /// </summary>
    [Fact]
    public void TheManagedReceiverDeliversWhatTheCDelivered()
    {
        ReceiverAnswer was = Recording().Receiver;

        var frames = new List<string>();
        var outbound = new Silent();

        var managed = new ManagedVideoReceiver(
            (frame, lost, recovered) =>
            {
                frames.Add(Hex(frame.ToArray()));
                return true;
            },
            outbound);

        managed.StreamInfo(FramePathOracleCases.ReceiverHeader());

        for (ushort i = 0; i < FramePathOracleCases.ReceiverUnits; i++)
        {
            managed.AvPacket(
                FramePathOracleCases.ReceiverFrame, i, FramePathOracleCases.ReceiverUnits, 0,
                FramePathOracleCases.Unit(FramePathOracleCases.ReceiverPayload, 0, seed: 40 + i));
        }

        Assert.Equal(was.Frames.Count, frames.Count);
        for (int i = 0; i < frames.Count; i++)
            Assert.Equal(was.Frames[i], frames[i]);

        Assert.Equal(was.FramesLost, managed.FramesLostTotal);

        // And nothing was reported, which is what makes the comparison possible at all: the C's
        // receiver is handed a zeroed session, so a case that reported would dereference nothing.
        Assert.Empty(outbound.Corrupt);
        Assert.Equal(0, outbound.IdrRequests);
    }

    /// <summary>
    /// PP842: AND THE RECORDING IS STILL WHAT THE WRAPPERS SAY, on a build that can ask.
    ///
    /// This is what keeps the file from becoming a fossil. A recorded second opinion nobody checks is
    /// a table somebody typed, and the whole reason it is worth having is that it came off the C - so
    /// while a build carrying the frame path exists, it is re-derived and compared. It declines where
    /// the wrappers are absent, which is the one guard the flip leaves standing here and the only one
    /// whose absence costs nothing: what it protects is asserted above on every build.
    /// </summary>
    [Fact]
    public void TheRecordingIsWhatTheWrappersStillSay()
    {
        if (!ShimFramePathShape.OracleIsAvailable())
            return;

        FramePathOracleRecording? taken = FramePathOracleRecorder.Take();
        Assert.NotNull(taken);

        FramePathOracleRecording was = Recording();

        // Field by field, because a record's generated equality compares its list members by
        // reference - two identical recordings would differ, and a difference that is always there
        // is a check nobody can act on.
        Assert.Equal(was.Decodes, taken.Decodes);

        Assert.Equal(was.Matrices.Count, taken.Matrices.Count);
        for (int i = 0; i < was.Matrices.Count; i++)
        {
            Assert.Equal(was.Matrices[i].K, taken.Matrices[i].K);
            Assert.Equal(was.Matrices[i].M, taken.Matrices[i].M);
            Assert.Equal(was.Matrices[i].Entries, taken.Matrices[i].Entries);
        }

        Assert.Equal(was.Assemblies.Count, taken.Assemblies.Count);
        for (int i = 0; i < was.Assemblies.Count; i++)
        {
            Assert.Equal(was.Assemblies[i].Name, taken.Assemblies[i].Name);
            Assert.Equal(was.Assemblies[i].Result, taken.Assemblies[i].Result);
            Assert.Equal(was.Assemblies[i].Frame, taken.Assemblies[i].Frame);
        }

        Assert.Equal(was.SecondFlush.First, taken.SecondFlush.First);
        Assert.Equal(was.SecondFlush.Second, taken.SecondFlush.Second);
        Assert.Equal(was.SecondFlush.SecondResult, taken.SecondFlush.SecondResult);

        Assert.Equal(was.Receiver.Frames, taken.Receiver.Frames);
        Assert.Equal(was.Receiver.FramesLost, taken.Receiver.FramesLost);
    }

    /// <summary>An outbound that records rather than sends, because the cases must not report.</summary>
    private sealed class Silent : IVideoReceiverOutbound
    {
        public List<(ushort From, ushort To)> Corrupt { get; } = [];

        public int IdrRequests { get; private set; }

        public void SendCorruptFrame(ushort from, ushort to) => Corrupt.Add((from, to));

        public bool SendIdrRequest()
        {
            IdrRequests++;
            return true;
        }

        public void FecFailure(int frameIndex, bool idrRequestSent)
        {
        }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
