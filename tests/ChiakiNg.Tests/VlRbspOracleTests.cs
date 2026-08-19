using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the managed bit reader, driven through the same operation sequences as vl_rbsp and compared
/// after every one.
///
/// The two slice-header parsers are 387 lines that do almost nothing except call these functions, so
/// this is the layer worth getting right first. It is also the layer where an example-based test
/// proves nothing: the interesting inputs are the ones with an emulation-prevention byte landing on
/// a fill boundary, and nobody constructs those by hand.
///
/// The payload's ADDRESS ALIGNMENT is part of the input here, which is not obvious and is the reason
/// the oracle takes it as a parameter. See <see cref="TheAlignmentChangesTheStateButNotTheOutput"/>.
/// </summary>
public class VlRbspOracleTests
{
    private readonly ITestOutputHelper output;

    public VlRbspOracleTests(ITestOutputHelper output) => this.output = output;

    private sealed class Lcg(uint seed)
    {
        private uint state = seed == 0 ? 1u : seed;

        public uint Next()
        {
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        public int Next(int bound) => (int)(Next() % (uint)bound);
    }

    /// <summary>
    /// Payloads built to hit the cases that matter: start codes, emulation-prevention sequences,
    /// runs of zeroes that make a long ue(v) prefix, and lengths either side of the four-byte fill.
    /// </summary>
    private static IEnumerable<byte[]> Corpus()
    {
        yield return [];
        yield return [0x00];
        yield return [0x00, 0x00, 0x01];
        yield return [0x00, 0x00, 0x00, 0x01];
        yield return [0x00, 0x00, 0x03];
        yield return [0x00, 0x00, 0x03, 0x01];
        yield return [0x67, 0x42, 0x00, 0x1f];
        yield return [0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1f, 0x8c, 0x8d, 0x40];
        yield return [0x00, 0x00, 0x01, 0x67, 0x00, 0x00, 0x03, 0x01, 0x02, 0x03, 0x04];
        yield return [0xff, 0xff, 0xff, 0xff, 0xff];
        yield return [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        yield return [0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        yield return [0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00];

        // Every length from 1 to 12 of a repeating pattern, so a fill boundary falls in each place.
        for (int n = 1; n <= 12; n++)
        {
            var bytes = new byte[n];
            for (int i = 0; i < n; i++)
                bytes[i] = (byte)(i % 3 == 2 ? 0x03 : 0x00);
            yield return bytes;
        }
    }

    private static readonly uint[] widths = [1, 2, 3, 5, 8, 16, 17, 24, 31, 32];

    /// <summary>
    /// The whole corpus, at every alignment, through a fixed script of reads. Every result and the
    /// overrun flag are compared; a divergence names the payload, the alignment and the step.
    /// </summary>
    [Theory]
    [InlineData(uint.MaxValue)]
    [InlineData(8u)]
    [InlineData(24u)]
    [InlineData(64u)]
    public void EveryPayloadReadsTheSameThroughBoth(uint numBits)
    {
        foreach (byte[] payload in Corpus())
        {
            for (int alignment = 0; alignment < 4; alignment++)
            {
                using var native = new NativeRbsp(payload, numBits, alignment);
                var managed = new VlRbsp(new VlVlc(payload, alignment), numBits);

                string where =
                    $"payload=[{Convert.ToHexString(payload)}] alignment={alignment} numBits={numBits}";

                Assert.Equal(alignment, native.Alignment);

                foreach (uint width in widths)
                {
                    Assert.Equal(native.HasBits(width), managed.HasBits(width));
                    Assert.Equal(native.U(width), managed.U(width));
                    Assert.Equal(native.Overrun, managed.Overrun);
                }

                Assert.Equal(native.Ue(), managed.Ue());
                Assert.Equal(native.Overrun, managed.Overrun);
                Assert.Equal(native.Se(), managed.Se());
                Assert.Equal(native.Overrun, managed.Overrun);
                Assert.Equal(native.MoreData(), managed.MoreData());
                Assert.Equal(native.ValidBits, managed.Nal.ValidBits);
                Assert.Equal(native.BitsLeft, managed.Nal.BitsLeft);
                Assert.True(native.Overrun == managed.Overrun, where);
            }
        }
    }

    /// <summary>
    /// Random payloads through random read sequences, which is what finds the cases the corpus above
    /// does not think of. Fixed seeds, so a failure reproduces.
    /// </summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(17u)]
    [InlineData(4242u)]
    public void RandomPayloadsAndReadsAgree(uint seed)
    {
        var rng = new Lcg(seed);

        for (int round = 0; round < 60; round++)
        {
            int length = rng.Next(20);
            var payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                // Weighted towards 0x00, 0x01 and 0x03, which is where the escape and start-code
                // logic lives - uniform bytes almost never produce those sequences.
                payload[i] = rng.Next(3) switch
                {
                    0 => 0x00,
                    1 => (byte)(rng.Next(2) == 0 ? 0x01 : 0x03),
                    _ => (byte)rng.Next(256),
                };
            }

            int alignment = rng.Next(4);
            uint numBits = rng.Next(4) == 0 ? (uint)(8 * rng.Next(8)) : uint.MaxValue;
            if (numBits == 0)
                numBits = uint.MaxValue;

            using var native = new NativeRbsp(payload, numBits, alignment);
            var managed = new VlRbsp(new VlVlc(payload, alignment), numBits);

            for (int step = 0; step < 40; step++)
            {
                string where =
                    $"seed={seed} round={round} step={step} "
                    + $"payload=[{Convert.ToHexString(payload)}] align={alignment} numBits={numBits}";

                switch (rng.Next(5))
                {
                    case 0:
                        uint n = widths[rng.Next(widths.Length)];
                        Assert.Equal(native.U(n), managed.U(n));
                        break;
                    case 1:
                        Assert.Equal(native.Ue(), managed.Ue());
                        break;
                    case 2:
                        Assert.Equal(native.Se(), managed.Se());
                        break;
                    case 3:
                        Assert.Equal(native.MoreData(), managed.MoreData());
                        break;
                    default:
                        uint ask = (uint)rng.Next(33);
                        Assert.Equal(native.HasBits(ask), managed.HasBits(ask));
                        break;
                }

                Assert.True(native.Overrun == managed.Overrun, where);
                Assert.True(native.ValidBits == managed.Nal.ValidBits, where);
                Assert.True(native.BitsLeft == managed.Nal.BitsLeft, where);
            }
        }
    }

    /// <summary>
    /// The alignment finding, measured rather than argued.
    ///
    /// vl_vlc_align_data_ptr consumes bytes one at a time until the data pointer is dword-aligned,
    /// so the number of bits valid after init depends on where the payload happened to be placed -
    /// and vl_rbsp_init's emulation-prevention scan is bounded by that number. This walks the corpus
    /// at all four alignments and reports whether the STATE differs, whether the OUTPUT differs, and
    /// which payloads do it.
    /// </summary>
    [Fact]
    public void TheAlignmentChangesTheStateButNotTheOutput()
    {
        var stateDiffers = new List<string>();
        var outputDiffers = new List<string>();

        foreach (byte[] payload in Corpus())
        {
            var validBits = new List<uint>();
            var reads = new List<string>();

            for (int alignment = 0; alignment < 4; alignment++)
            {
                using var native = new NativeRbsp(payload, uint.MaxValue, alignment);
                validBits.Add(native.ValidBits);

                var seen = new List<string>();
                foreach (uint width in widths)
                    seen.Add(native.U(width).ToString());
                seen.Add(native.Ue().ToString());
                seen.Add(native.Se().ToString());
                seen.Add(native.Overrun.ToString());
                reads.Add(string.Join(",", seen));
            }

            string name = payload.Length == 0 ? "<empty>" : Convert.ToHexString(payload);
            if (validBits.Distinct().Count() > 1)
                stateDiffers.Add($"{name}: validBits {string.Join("/", validBits)}");
            if (reads.Distinct().Count() > 1)
                outputDiffers.Add($"{name}: {string.Join(" | ", reads.Distinct())}");
        }

        foreach (string line in stateDiffers)
            output.WriteLine("state: " + line);
        foreach (string line in outputDiffers)
            output.WriteLine("OUTPUT: " + line);

        // The state does differ - that is the point of the parameter existing.
        Assert.NotEmpty(stateDiffers);

        // The output does not, which is what licenses the managed reader to emulate one alignment
        // and the parsers above it to ignore the question. If this ever fails, libchiaki's header
        // parse depends on an allocator's choice and that is a finding, not a test to relax.
        Assert.Empty(outputDiffers);
    }

    /// <summary>
    /// PP68's two exits, behaviourally. A truncated header used to spin in ue(v) for ever because
    /// the depleted bit buffer yields zeroes; now the prefix cannot outrun what is left, and cannot
    /// be 32 zeroes either - `1 &lt;&lt; bits` is undefined past 31, so the cap is a correctness bound.
    /// </summary>
    [Fact]
    public void ATruncatedHeaderOverrunsRatherThanSpinning()
    {
        // Eight bytes of zeroes: a ue(v) prefix that never finds its 1 bit.
        byte[] payload = [0, 0, 0, 0, 0, 0, 0, 0];

        using var native = new NativeRbsp(payload);
        var managed = new VlRbsp(new VlVlc(payload));

        Assert.Equal(native.Ue(), managed.Ue());
        Assert.True(managed.Overrun);
        Assert.Equal(native.Overrun, managed.Overrun);

        // And a read past the end is zero with the flag set, not an exception and not a hang.
        Assert.Equal(native.U(8), managed.U(8));
        Assert.True(managed.Overrun);
    }

    /// <summary>
    /// The clamp PP70 added, reached from managed code: once a read has gone past the end the signed
    /// count climbs above 32, and the unclamped `32 - invalidBits` read as unsigned is about four
    /// billion - "plenty left", at exactly the moment there is nothing.
    /// </summary>
    [Fact]
    public void ValidBitsIsClampedRatherThanWrapping()
    {
        byte[] payload = [0xff];
        using var native = new NativeRbsp(payload);
        var managed = new VlRbsp(new VlVlc(payload));

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(native.U(8), managed.U(8));
            Assert.Equal(native.ValidBits, managed.Nal.ValidBits);
            Assert.True(managed.Nal.ValidBits <= 32, $"validBits ran away to {managed.Nal.ValidBits}");
        }
    }
}
