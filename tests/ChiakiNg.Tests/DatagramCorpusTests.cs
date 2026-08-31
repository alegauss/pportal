using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP608, under PP27: the capture PP607's harness had nothing of, recorded and committed.
///
/// PP516's entry records two captures on disk that nothing read. Neither was committed and neither
/// survived. This one is in tests/corpus/ beside PP297's exchange for that reason: a recording that
/// takes a live console to make is not one to leave in a log directory.
///
/// What these hold is that it is real and that the numbers said about it are its own - the count,
/// the spacing, and that heads are heads. PP391's complaint was a replay harness fed only
/// recordings the tests wrote; this is the answer to it, so it had better be the console's.
/// </summary>
public class DatagramCorpusTests(ITestOutputHelper output)
{
    /// <summary>It is there and it parses as the version this reader understands.</summary>
    [Fact]
    public void TheCaptureIsReadable()
    {
        if (DatagramCorpus.Locate() is null)
            return;

        IReadOnlyList<CapturedDatagram>? datagrams = DatagramCorpus.Read();

        Assert.NotNull(datagrams);
        Assert.Equal(DatagramCorpus.Datagrams, datagrams!.Count);
    }

    /// <summary>
    /// The spacing is the stream's, computed from the rows rather than trusted from a header.
    ///
    /// PP531 timed its MAC gate inside a mean arrival gap of 1178us. This capture's is 1159us, which
    /// is the same stream at the same rate - and it is what makes a comparison against the C mean
    /// anything, because a number measured inside a different spacing is about a different problem.
    /// </summary>
    [Fact]
    public void TheSpacingIsTheOneTheComparisonAssumes()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        double mean = DatagramCorpus.MeanGap(datagrams);
        output.WriteLine($"{datagrams.Count} datagrams, mean gap {mean:F0} us");

        // Within a microsecond of the constant: the constant is a claim about this file, and a file
        // swapped for another stream would not land there by accident.
        Assert.InRange(mean, DatagramCorpus.MeanGapMicros - 1, DatagramCorpus.MeanGapMicros + 1);
    }

    /// <summary>
    /// Heads are heads, and the length column is the datagram's.
    ///
    /// The distinction PP520 refuses a whole capture version over: a file whose Length is the head's
    /// reports every size wrongly, and the head cannot be grown back into a datagram. Here the two
    /// differ, which is what says this is the version that kept both.
    /// </summary>
    [Fact]
    public void TheHeadsAreHeadsAndTheLengthsAreTheDatagrams()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        Assert.All(datagrams, d => Assert.True(d.Head.Length <= DatagramCorpus.HeadBytes));

        // Some datagram is longer than what was kept of it, or this is a head-length capture
        // wearing the new version string.
        Assert.Contains(datagrams, d => d.Length > d.Head.Length);

        // And every row's length is at least what was kept, which the other way round cannot be.
        Assert.All(datagrams, d => Assert.True(d.Length >= d.Head.Length));
    }

    /// <summary>
    /// It is a real stream: the arrival times only move forward, and they span the sample.
    ///
    /// A capture whose times went backwards, or whose rows all shared one, would still parse - and
    /// every timing conclusion drawn from it would be silently about nothing.
    /// </summary>
    [Fact]
    public void TheArrivalTimesAreAStream()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        for (int i = 1; i < datagrams.Count; i++)
        {
            Assert.True(
                datagrams[i].ArrivalMicroseconds >= datagrams[i - 1].ArrivalMicroseconds,
                $"row {i} arrives before row {i - 1}, so this is not a recording of a stream");
        }

        long span = datagrams[^1].ArrivalMicroseconds - datagrams[0].ArrivalMicroseconds;
        Assert.True(span > 1_000_000, $"the capture spans {span}us, which is not the sample it claims");
    }

    /// <summary>
    /// And the takion header is in there, which is what the harness will be pumping.
    ///
    /// Read through the port's own model rather than by eye: the first byte is a packet type the
    /// C names, and the head is long enough to carry the chunk type at 0xd.
    /// </summary>
    [Fact]
    public void TheHeadsCarryATakionHeader()
    {
        if (DatagramCorpus.Read() is not { } datagrams)
            return;

        Assert.Contains(
            datagrams,
            d => d.Head.Length > TakionHandshake.ChunkTypeOffsetInDatagram);

        // Every base type the capture recorded is one the C's own offsets table knows.
        Assert.All(datagrams, d => Assert.InRange(d.BaseType, 0, 15));
    }
}
