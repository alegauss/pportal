using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP287: the managed encode and decode, judged by what judged the C.
///
/// PP286 agreed the field and the coding matrix with jerasure's, which eliminated two of the three
/// suspects a failing recorded case leaves. This is the third, and it is the one the recorded cases
/// were kept for: sixty-four erasure patterns a real stream produced, with the bytes that had to
/// come back.
///
/// Every case runs through BOTH decoders here, not because the C's answer is in doubt but because
/// "the managed one recovered it" and "the two agree" are different claims. The first can be true
/// of an implementation that repairs a frame the C would have refused.
/// </summary>
public class FecCodecTests(ITestOutputHelper output)
{
    private static IReadOnlyList<FecCase> Cases { get; } = Load();

    private static IReadOnlyList<FecCase> Load()
    {
        string? file = FecVectors.Locate();
        return file is null ? [] : FecVectors.Parse(file);
    }

    public static TheoryData<int> CaseIndices()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < Cases.Count; i++)
            data.Add(i);
        return data;
    }

    /// <summary>The field tables the native side reads live in a global lib_init fills.</summary>
    private static void EnsureNativeField()
        => Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

    /// <summary>
    /// THE ASSERTION. The managed decoder puts back the bytes the stream sent, on every case.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void TheManagedDecoderRecoversTheRecordedCase(int index)
    {
        FecCase recorded = Cases[index];
        Assert.True(
            Fec.Recovers(recorded, managed: true),
            $"k={recorded.K} m={recorded.M} unit={recorded.UnitSize} "
                + $"lost=[{string.Join(",", recorded.Erasures)}]");
    }

    /// <summary>
    /// And it agrees with the C on the whole FRAME, not just on the verdict.
    ///
    /// Comparing verdicts is not a differential test and this was written that way first. A decoder
    /// that repairs nothing and reports success agrees with a correct one on every recorded case,
    /// because the bytes it is asked about are the ones already in the buffer - measured, by
    /// neutering the managed decoder and watching exactly these sixty-four stay green while a
    /// hundred and thirty-three others went red.
    ///
    /// The whole frame includes the PARITY units, which the recorded cases deliberately do not
    /// assert. Here they are the strongest part of the comparison: the stream never reads them
    /// back, so nothing else in the suite would notice the two implementations rebuilding them
    /// differently.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void BothDecodersProduceTheSameFrame(int index)
    {
        EnsureNativeField();

        FecCase recorded = Cases[index];
        byte[]? native = Fec.Decode(recorded, managed: false);
        byte[]? managed = Fec.Decode(recorded, managed: true);

        Assert.True(native is not null, "the C refused a recorded case");
        Assert.True(managed is not null, $"the managed decoder refused: k={recorded.K} m={recorded.M}");
        Assert.Equal(native, managed);
    }

    /// <summary>
    /// The spare-parity path, which the recorded cases never reach.
    ///
    /// All sixty-four have EXACTLY m erasures - 57 are (2 lost, m=2), and the rest are 1/1, 3/3 and
    /// 4/4. Counted, not assumed: the first version of this test declared one extra erasure on top
    /// of each recorded case and skipped where there was no room, which meant it skipped all
    /// sixty-four and asserted nothing at all. It looked like coverage for exactly as long as
    /// nobody counted, which is PP271's lesson arriving from inside a new test.
    ///
    /// So the case is built here instead. Lose fewer units than there is parity for, then tell the
    /// decoder MORE were lost than were: it has to rebuild a unit whose bytes are already correct,
    /// from rows it was told to distrust, and land on the same answer. A decoder that returns the
    /// buffer untouched passes; one doing arithmetic has to do it right.
    /// </summary>
    [Theory]
    [InlineData(6, 3, 64)]
    [InlineData(8, 4, 128)]
    [InlineData(12, 4, 96)]
    public void DeclaringMoreErasuresThanHappenedStillRecovers(int k, int m, int unitSize)
    {
        int stride = FecVectors.StrideFor(unitSize);
        var frame = new byte[stride * (k + m)];

        var random = new Random(Seed: (k * 31) + m);
        for (int i = 0; i < k; i++)
            random.NextBytes(frame.AsSpan(i * stride, unitSize));

        var original = frame.AsSpan(0, k * stride).ToArray();

        FecCodec.Encode(frame, unitSize, stride, k, m);
        for (int i = m - 1; i >= 0; i--)
        {
            frame.AsSpan((k * unitSize) + (i * unitSize), unitSize)
                .CopyTo(frame.AsSpan((k + i) * stride, unitSize));
        }

        // One unit actually lost, m declared. The extra m-1 are units whose bytes are still there.
        frame.AsSpan(0, unitSize).Fill(0x42);
        var declared = new uint[m];
        for (int i = 0; i < m; i++)
            declared[i] = (uint)i;

        Assert.True(FecCodec.Decode(frame, unitSize, stride, k, m, declared), $"{k}x{m} did not decode");

        for (int i = 0; i < k; i++)
        {
            Assert.True(
                frame.AsSpan(i * stride, unitSize).SequenceEqual(original.AsSpan(i * stride, unitSize)),
                $"unit {i} of {k} came back wrong with {m} declared and 1 lost");
        }
    }

    /// <summary>
    /// The encode, against the decoder that has just been agreed.
    ///
    /// There is no recorded oracle for encoding - the cases carry frames a console produced, and
    /// the parity in them is what the C would rebuild rather than what it wrote. So the round trip
    /// is the assertion: encode parity over known data, blank a unit, decode, and the data is back.
    /// A wrong encode cannot survive that, because the decode it feeds was agreed independently.
    /// </summary>
    [Theory]
    [InlineData(4, 2, 64)]
    [InlineData(8, 2, 128)]
    [InlineData(10, 3, 32)]
    [InlineData(15, 4, 256)]
    public void EncodeAndDecodeRoundTrip(int k, int m, int unitSize)
    {
        int stride = FecVectors.StrideFor(unitSize);
        var frame = new byte[stride * (k + m)];

        // Deterministic, and not all one byte: a frame of zeroes round-trips through an encoder
        // that multiplies by nothing at all.
        var random = new Random(Seed: (k * 1000) + (m * 10) + unitSize);
        for (int i = 0; i < k; i++)
            random.NextBytes(frame.AsSpan(i * stride, unitSize));

        var original = frame.AsSpan(0, k * stride).ToArray();

        // The encode writes parity PACKED at k*unitSize, which is where the C puts it. Laid out at
        // stride afterwards, because that is where the decoder reads it from - the two disagree
        // whenever stride differs from unitSize, and reproducing that is PP30's job rather than
        // repairing it.
        FecCodec.Encode(frame, unitSize, stride, k, m);
        for (int i = m - 1; i >= 0; i--)
        {
            frame.AsSpan((k * unitSize) + (i * unitSize), unitSize)
                .CopyTo(frame.AsSpan((k + i) * stride, unitSize));
        }

        // Lose as many as the parity allows, from the front, which is the worst case for a decoder
        // that special-cases "no data unit lost".
        var lost = new uint[m];
        for (int i = 0; i < m; i++)
        {
            lost[i] = (uint)i;
            frame.AsSpan(i * stride, unitSize).Fill(0x42);
        }

        Assert.True(FecCodec.Decode(frame, unitSize, stride, k, m, lost), $"{k}x{m} did not decode");

        for (int i = 0; i < k; i++)
        {
            Assert.True(
                frame.AsSpan(i * stride, unitSize).SequenceEqual(original.AsSpan(i * stride, unitSize)),
                $"unit {i} of {k} came back wrong");
        }

        output.WriteLine($"{k}x{m} at {unitSize} bytes: {m} units lost and rebuilt");
    }

    /// <summary>
    /// More losses than parity is answered, not thrown. A lossy connection reaches that state
    /// normally and the frame is simply dropped.
    /// </summary>
    [Fact]
    public void MoreErasuresThanParityIsRefused()
    {
        const int K = 4;
        const int M = 2;
        const int Unit = 32;
        int stride = FecVectors.StrideFor(Unit);

        var frame = new byte[stride * (K + M)];
        Assert.False(FecCodec.Decode(frame, Unit, stride, K, M, [0, 1, 2]));

        // ...and an index outside the frame is refused rather than read.
        Assert.False(FecCodec.Decode(frame, Unit, stride, K, M, [99]));
    }
}
