using System.IO;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP834: the artefact that holds what this machine SENT, which no capture in this tree did.
///
/// Both relay call sites recorded arrivals only, each saying the same true thing: the capture is a
/// replay corpus, and offering the sends as well would produce a file the replay reads as twice
/// the traffic. PP813 is what that cost - a senkusha command leaving on the wrong takion channel
/// for six tasks, investigated by comparing the port's ping against bytes built through the C's
/// formatter, which is a model of the send rather than the send.
/// </summary>
public class WireLogTests(ITestOutputHelper output)
{
    private static string Scratch()
        => Path.Combine(Path.GetTempPath(), $"wire-{Guid.NewGuid():N}.txt");

    /// <summary>
    /// Both directions and both legs land, and a row says which it was.
    ///
    /// The direction is the whole point and the leg is what PP813 needed beside it: senkusha's
    /// commands were wrong while the stream leg ran perfectly, so a log that mixed them would have
    /// answered the wrong question about the right file.
    /// </summary>
    [Fact]
    public void EveryRowSaysItsLegAndItsDirection()
    {
        string path = Scratch();
        var clock = 0L;

        try
        {
            using (var log = new WireLog(path, () => clock += 100))
            {
                log.Offer([0x01, 0x02], fromConsole: false, RelayLeg.Senkusha);
                log.Offer([0xaa], fromConsole: true, RelayLeg.Stream);

                Assert.Equal(2, log.Rows);

                // The half nothing recorded before, counted on its own.
                Assert.Equal(1, log.Sent);
            }

            string[] lines = File.ReadAllLines(path);

            foreach (string one in lines)
                output.WriteLine(one);

            // A header a person can read, then a row each.
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("#", lines[0], StringComparison.Ordinal);

            Assert.Equal("100 senkusha out 2 0102", lines[1]);
            Assert.Equal("200 stream in  1 AA", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// It is flushed by the dispose, which is what a run that ends normally does.
    ///
    /// The writer buffers on purpose - SessionRelay's own note is that a recorder blocking in the
    /// callback delays the stream it is recording - so the flush has to happen somewhere, and a
    /// file that is empty until the process exits is one nobody trusts.
    /// </summary>
    [Fact]
    public void TheRowsAreOnDiskOnceItIsDisposed()
    {
        string path = Scratch();

        try
        {
            var log = new WireLog(path, () => 7);
            log.Offer([0xff], fromConsole: true, RelayLeg.Stream);
            log.Dispose();

            Assert.Contains("7 stream in  1 FF", File.ReadAllText(path), StringComparison.Ordinal);

            // And a second dispose is not a second flush onto a closed writer.
            log.Dispose();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// PP834: the run joins it to the relay, and leaves the corpus taking arrivals alone.
    ///
    /// Read out of the source because the join is inside a run that wants a console. BOTH halves
    /// are asserted: a wire log that also fed the capture would be the column §PP834 refused, and
    /// one the relay never reached would be a file of nothing.
    /// </summary>
    [Fact]
    public void TheRunFeedsItBothDirectionsAndTheCorpusOnlyArrivals()
    {
        string text = File.ReadAllText(
            SanitizerSource.LocateRelative(@"app\Session\StreamRun.cs")
            ?? throw new InvalidOperationException("the run is not in this checkout."));

        // Both directions into the log: no `fromConsole` guard on this one.
        Assert.Matches(@"(?m)^\s*wire\?\.Offer\(bytes, fromConsole, leg\);", text);

        // And the corpus still guarded, which is the half a replay depends on.
        Assert.Matches(@"(?m)^\s*if \(fromConsole\)\s*$", text);
    }
}
